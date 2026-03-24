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
    private string? _outputFormat;
    private bool _accurateCut;
    private bool _forceReencode;
    private MediaInfo? _probedInfo;
    private IProgress<FFmpegProgress>? _externalProgress;
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
    // Trim parameters
    // ───────────────────────────────────────────────
    public ClipBuilder Start(TimeSpan start)
    {
        _start = start >= TimeSpan.Zero ? start : null;
        return this;
    }

    public ClipBuilder Duration(TimeSpan length)
    {
        _duration = length > TimeSpan.Zero ? length : null;
        return this;
    }

    public ClipBuilder End(TimeSpan endTime)
    {
        _end = endTime > TimeSpan.Zero ? endTime : null;
        return this;
    }

    public ClipBuilder AccurateCut(bool accurate = true)
    {
        _accurateCut = accurate;
        return this;
    }

    public ClipBuilder ForceReencode(bool force = true)
    {
        _forceReencode = force;
        return this;
    }

    public ClipBuilder Format(string format)
    {
        _outputFormat = format?.TrimStart('.').ToLowerInvariant();
        return this;
    }

    public ClipBuilder WithProgress(IProgress<FFmpegProgress> progress)
    {
        _externalProgress = progress;
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

        // Normalize end time
        if (_end.HasValue && !_duration.HasValue)
        {
            if (_start.HasValue)
                _duration = _end.Value - _start.Value;
            else
                _start = TimeSpan.Zero;
        }

        if (_duration.HasValue && _duration.Value <= TimeSpan.Zero)
            throw new ArgumentException("Duration must be positive", nameof(_duration));

        // Enable structured progress
        _cmd.WithProgressUrl("pipe:1");
        _cmd.WithNoStats(true);

        ConfigureClipCommand();

        var args = _cmd.Build();

        _options.Logger?.LogDebug("Clip command: {Command}", _cmd.BuildAsString());

        var stopwatch = Stopwatch.StartNew();
        FFmpegProgress? finalProgress = null;

        var progressWrapper = new Progress<FFmpegProgress>(p =>
        {
            _lastProgress = p;
            finalProgress = p;
            _externalProgress?.Report(p);
        });

        // For progress calculation, use clip duration if known, otherwise full file duration
        TimeSpan? progressDuration = _duration ?? _probedInfo?.Duration;

        try
        {
            var processResult = await _runner.RunFFmpegAsync(
                arguments: args,
                options: _options,
                progress: progressWrapper,
                knownDuration: progressDuration,
                cancellationToken: ct);

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
            // Accurate seeking (after -i) → usually forces re-encode
            if (_start.HasValue)
                _cmd.SeekOutput(_start.Value);

            if (_duration.HasValue)
                _cmd.Duration(_duration.Value);
            else if (_end.HasValue)
                _cmd.ToTime(_end.Value);

            if (canCopy)
                _cmd.CopyAllStreams();
            else
                ApplyReencodeDefaults();
        }
        else
        {
            // Fast seeking (before -i) → best chance for stream copy
            if (_start.HasValue)
                _cmd.SeekInput(_start.Value);

            if (_duration.HasValue)
                _cmd.Duration(_duration.Value);
            else if (_end.HasValue)
                _cmd.ToTime(_end.Value);

            if (canCopy)
            {
                _cmd.CopyAllStreams();
                _options.Logger?.LogInformation("Clip: Fast seek + stream copy enabled");
            }
            else
            {
                ApplyReencodeDefaults();
                _options.Logger?.LogInformation("Clip: Re-encoding required");
            }
        }

        _cmd.Output(_outputPath!);
    }

    private bool CanUseStreamCopy()
    {
        if (_probedInfo == null) return false;

        // Pure audio clip → almost always safe to copy
        if (_probedInfo.VideoStream == null && _probedInfo.AudioStream != null)
            return true;

        // Accurate cut usually breaks stream copy
        if (_accurateCut) return false;

        // For video: simple check (can be made stricter later)
        return true;
    }

    private void ApplyReencodeDefaults()
    {
        _cmd.VideoCodec("libx264");
        _cmd.Crf(23);
        _cmd.Preset("medium");
        _cmd.AudioCodec("aac");
        _cmd.AddArgument("-b:a", "192k");

        if (_outputFormat != null)
        {
            // Extension usually determines container, but we can force if needed
        }
    }

    public string GetCommandPreview() => _cmd.BuildAsString();
}