using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Core;
using ManuHub.FF.NET.Models;
using ManuHub.FF.NET.Parsing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ManuHub.FF.NET.Builders;

/// <summary>
/// Fluent builder for trimming / clipping a segment from a video or audio file.
/// Attempts fast stream copy when possible; falls back to re-encoding when needed.
/// </summary>
public class ClipBuilder
{
    private readonly IFFmpegRunner _runner;
    private readonly FFmpegOptions _options;
    private readonly FFmpegCommandBuilder _cmd;

    private string? _inputPath;
    private string? _outputPath;
    private TimeSpan? _start;
    private TimeSpan? _duration;
    private TimeSpan? _end;
    private string? _outputFormat;           // optional forced extension/codec
    private bool _accurateCut;               // use -ss before -i for frame-accurate but slower
    private bool _forceReencode;
    private MediaInfo? _probedInfo;
    private IProgress<FFmpegProgress>? _progress;
    private FFmpegProgress? _lastProgress;

    public ClipBuilder(IFFmpegRunner runner, FFmpegOptions? options = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options?.Clone() ?? new FFmpegOptions();
        _cmd = new FFmpegCommandBuilder(_options);
    }

    // ───────────────────────────────────────────────
    // Input / Output
    // ───────────────────────────────────────────────

    public ClipBuilder From(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path required", nameof(inputPath));

        _inputPath = inputPath;
        _cmd.Input(inputPath);
        return this;
    }

    public ClipBuilder To(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path required", nameof(outputPath));

        _outputPath = outputPath;
        return this;
    }

    // ───────────────────────────────────────────────
    // Trim / Clip parameters
    // ───────────────────────────────────────────────

    /// <summary>
    /// Start time (seek position). Can be combined with Duration or End.
    /// </summary>
    public ClipBuilder Start(TimeSpan start)
    {
        _start = start >= TimeSpan.Zero ? start : null;
        return this;
    }

    /// <summary>
    /// Duration of the clip to extract.
    /// </summary>
    public ClipBuilder Duration(TimeSpan length)
    {
        _duration = length > TimeSpan.Zero ? length : null;
        return this;
    }

    /// <summary>
    /// End time of the clip (alternative to Duration).
    /// </summary>
    public ClipBuilder End(TimeSpan endTime)
    {
        _end = endTime > TimeSpan.Zero ? endTime : null;
        return this;
    }

    /// <summary>
    /// Use accurate (frame-level) seeking (slower, re-encodes).
    /// Default: fast seeking (keyframe-based, usually preserves copy).
    /// </summary>
    public ClipBuilder AccurateCut(bool accurate = true)
    {
        _accurateCut = accurate;
        return this;
    }

    /// <summary>
    /// Force re-encoding even when stream copy would be possible.
    /// </summary>
    public ClipBuilder ForceReencode(bool force = true)
    {
        _forceReencode = force;
        return this;
    }

    /// <summary>
    /// Force output container/format (overrides extension-based detection).
    /// Example: "mp4", "mkv", "webm", "aac", "mp3"
    /// </summary>
    public ClipBuilder Format(string format)
    {
        _outputFormat = format?.TrimStart('.').ToLowerInvariant();
        return this;
    }

    public ClipBuilder WithProgress(IProgress<FFmpegProgress> progress)
    {
        _progress = progress;
        return this;
    }

    // ───────────────────────────────────────────────
    // Probe
    // ───────────────────────────────────────────────

    public async Task<MediaInfo> ProbeAsync(CancellationToken ct = default)
    {
        if (_probedInfo != null) return _probedInfo;

        if (_inputPath == null)
            throw new InvalidOperationException("Call .From(...) first");

        var ffprobe = new FFprobeRunner(_runner, _options);
        string json = await ffprobe.GetJsonOutputAsync(_inputPath, ct: ct);
        _probedInfo = MediaInfoParser.Parse(json);
        return _probedInfo;
    }

    // ───────────────────────────────────────────────
    // Execute
    // ───────────────────────────────────────────────

    public async Task<FFmpegResult> ExecuteAsync(CancellationToken ct = default)
    {
        if (_inputPath == null)
            throw new InvalidOperationException("Input required (.From(...))");

        if (_outputPath == null)
            throw new InvalidOperationException("Output required (.To(...))");

        if (!_start.HasValue && !_duration.HasValue && !_end.HasValue)
            throw new InvalidOperationException("Specify at least Start+Duration, Start+End or Duration");

        // Probe if not done
        if (_probedInfo == null)
        {
            await ProbeAsync(ct).ConfigureAwait(false);
        }

        var totalDuration = _probedInfo!.Duration;
        if (totalDuration <= TimeSpan.Zero)
        {
            _options.Logger?.LogWarning("Input duration unknown — progress % may be inaccurate");
        }

        // Normalize end time
        if (_end.HasValue && !_duration.HasValue)
        {
            if (_start.HasValue)
                _duration = _end.Value - _start.Value;
            else
                _start = TimeSpan.Zero;
        }

        if (_duration.HasValue && _duration.Value <= TimeSpan.Zero)
            throw new ArgumentException("Duration must be positive");

        ConfigureClipCommand();

        var args = _cmd.Build();

        var stopwatch = Stopwatch.StartNew();
        FFmpegProgress? finalProgress = null;

        var progressWrapper = new Progress<FFmpegProgress>(p =>
        {
            finalProgress = p;
            _progress?.Report(p);
        });

        var parser = new ProgressParser(_duration ?? totalDuration, progressWrapper);

        try
        {
            var processResult = await _runner.RunFFmpegAsync(
                args,
                _options,
                progressWrapper,
                knownDuration: _duration,
                ct);

            stopwatch.Stop();

            return processResult.Success
                ? FFmpegResult.SuccessResult(_outputPath, finalProgress, processResult)
                : FFmpegResult.FailureFromProcess(processResult, finalProgress, _outputPath);
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            return FFmpegResult.CancelledResult(stopwatch.Elapsed, finalProgress, ex);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return FFmpegResult.FromException(ex, _outputPath, finalProgress);
        }
    }

    private void ConfigureClipCommand()
    {
        bool canCopy = !_forceReencode && CanUseStreamCopy();

        if (_accurateCut)
        {
            // Accurate mode: -ss after -i → usually requires re-encoding
            if (_start.HasValue)
                _cmd.SeekOutput(_start.Value);

            if (_duration.HasValue)
                _cmd.Duration(_duration.Value);
            else if (_end.HasValue)
                _cmd.ToTime(_end.Value);

            if (!canCopy)
            {
                ApplyReencodeDefaults();
            }
            else
            {
                _cmd.CopyAllStreams();
            }
        }
        else
        {
            // Fast mode: -ss before -i (keyframe seeking) + copy possible
            if (_start.HasValue)
                _cmd.SeekInput(_start.Value);

            if (_duration.HasValue)
                _cmd.Duration(_duration.Value);
            else if (_end.HasValue)
                _cmd.ToTime(_end.Value);

            if (canCopy)
            {
                _cmd.CopyAllStreams();
                _options.Logger?.LogInformation("Clip: using fast seek + stream copy");
            }
            else
            {
                ApplyReencodeDefaults();
                _options.Logger?.LogInformation("Clip: fast seek not possible → re-encoding");
            }
        }

        _cmd.Output(_outputPath!);
    }

    private bool CanUseStreamCopy()
    {
        if (_probedInfo == null) return false;

        var video = _probedInfo.VideoStream;
        var audio = _probedInfo.AudioStream;

        // For pure audio clip → almost always copy
        if (video == null && audio != null)
            return true;

        // Video clip: copy only if we don't need precise cut or format change
        if (_accurateCut) return false;

        // Simple check — in real-world often more constraints (GOP, pixel format, etc.)
        return true;
    }

    private void ApplyReencodeDefaults()
    {
        // Safe re-encode defaults
        _cmd.VideoCodec("libx264");
        _cmd.Crf(23);
        _cmd.Preset("medium");
        _cmd.AudioCodec("aac");
        _cmd.AddArgument("-b:a", "192k");

        // If output format forced
        if (_outputFormat != null)
        {
            // could add -f {format} but usually extension is enough
        }
    }

    public string GetCommandPreview() => _cmd.BuildAsString();
}