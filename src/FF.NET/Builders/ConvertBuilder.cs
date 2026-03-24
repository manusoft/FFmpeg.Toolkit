using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Core;
using ManuHub.FF.NET.Models;
using ManuHub.FF.NET.Parsing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ManuHub.FF.NET.Builders;

/// <summary>
/// Fluent builder for common video/audio conversion/transcoding operations.
/// </summary>
/// <summary>
/// Fluent builder for common video/audio conversion/transcoding operations.
/// </summary>
public class ConvertBuilder
{
    private readonly IFFmpegRunner _runner;
    private readonly FFmpegOptions _options;
    private readonly FFmpegCommandBuilder _cmd;

    private string? _inputPath;
    private string? _outputPath;
    private TimeSpan? _knownDuration;
    private MediaInfo? _probedInfo;
    private bool _autoProbe = true;
    private IProgress<FFmpegProgress>? _externalProgress;
    private FFmpegProgress? _lastProgress;

    public MediaInfo? ProbedInfo => _probedInfo;

    public ConvertBuilder(IFFmpegRunner runner, FFmpegOptions? options = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options?.Clone() ?? new FFmpegOptions();
        _cmd = new FFmpegCommandBuilder(_options);
    }

    // ───────────────────────────────────────────────
    // Input / Output
    // ───────────────────────────────────────────────
    public ConvertBuilder From(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path cannot be empty", nameof(inputPath));

        _inputPath = inputPath;
        _cmd.Input(inputPath);
        return this;
    }

    public ConvertBuilder To(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path cannot be empty", nameof(outputPath));

        _outputPath = outputPath;
        _cmd.Output(outputPath);
        return this;
    }

    public ConvertBuilder Overwrite(bool overwrite = true)
    {
        _cmd.OverwriteOutput(overwrite);
        return this;
    }

    // ───────────────────────────────────────────────
    // Seeking / Duration
    // ───────────────────────────────────────────────
    public ConvertBuilder Seek(TimeSpan start)
    {
        _cmd.SeekInput(start);
        return this;
    }

    public ConvertBuilder Duration(TimeSpan duration)
    {
        _cmd.Duration(duration);
        return this;
    }

    public ConvertBuilder Trim(TimeSpan start, TimeSpan duration)
    {
        _cmd.SeekInput(start);
        _cmd.Duration(duration);
        return this;
    }

    // ───────────────────────────────────────────────
    // Video & Audio Settings
    // ───────────────────────────────────────────────
    public ConvertBuilder VideoCodec(string codec) { _cmd.VideoCodec(codec); return this; }
    public ConvertBuilder CopyVideo() { _cmd.CopyVideo(); return this; }
    public ConvertBuilder Crf(int crf) { _cmd.Crf(crf); return this; }
    public ConvertBuilder Preset(string preset) { _cmd.Preset(preset); return this; }
    public ConvertBuilder Scale(int width, int? height = null) { _cmd.Scale(width, height); return this; }
    public ConvertBuilder Fps(double fps) { _cmd.Fps(fps); return this; }

    public ConvertBuilder AudioCodec(string codec) { _cmd.AudioCodec(codec); return this; }
    public ConvertBuilder CopyAudio() { _cmd.CopyAudio(); return this; }
    public ConvertBuilder AudioBitrate(int kbps)
    {
        _cmd.AddArgument("-b:a", $"{kbps}k");
        return this;
    }

    // ───────────────────────────────────────────────
    // Subtitles / Metadata
    // ───────────────────────────────────────────────
    public ConvertBuilder BurnSubtitles(string subtitlePath)
    {
        _cmd.BurnSubtitles(subtitlePath);
        return this;
    }

    public ConvertBuilder CopySubtitles()
    {
        _cmd.AddArgument("-c:s", "mov_text");
        return this;
    }

    public ConvertBuilder Metadata(string key, string value)
    {
        _cmd.Metadata(key, value);
        return this;
    }

    // ───────────────────────────────────────────────
    // Progress & Probe
    // ───────────────────────────────────────────────
    public ConvertBuilder WithProgress(IProgress<FFmpegProgress> progress)
    {
        _externalProgress = progress;
        return this;
    }

    public ConvertBuilder WithKnownDuration(TimeSpan duration)
    {
        _knownDuration = duration;
        return this;
    }

    public ConvertBuilder AutoProbe(bool enable = true)
    {
        _autoProbe = enable;
        return this;
    }

    // ───────────────────────────────────────────────
    // Smart Copy Logic
    // ───────────────────────────────────────────────
    public ConvertBuilder SmartCopyIfPossible(bool forceReencode = false)
    {
        if (_probedInfo == null)
            throw new InvalidOperationException("Input must be probed first. Call ProbeInputAsync() or enable AutoProbe.");

        if (forceReencode) return this;

        bool isMp4 = _outputPath?.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) == true;
        if (!isMp4) return this;

        var video = _probedInfo.VideoStream;
        var audio = _probedInfo.AudioStream;

        bool canCopyVideo = video?.CodecName?.ToLowerInvariant() is "h264" or "avc" or "avc1"
                            && video.PixelFormat?.ToLowerInvariant() is "yuv420p" or null;

        bool canCopyAudio = audio?.CodecName?.ToLowerInvariant() is "aac" or "mp4a";

        if (canCopyVideo && canCopyAudio)
        {
            _cmd.CopyAllStreams();
            _cmd.AddArgument("-movflags", "+faststart");
            _options.Logger?.LogInformation("SmartCopy: Full stream copy with faststart");
        }
        else if (canCopyVideo)
        {
            _cmd.CopyVideo();
            _cmd.AudioCodec("aac");
            _cmd.AddArgument("-b:a", "192k");
            _cmd.AddArgument("-movflags", "+faststart");
        }
        else
        {
            _cmd.VideoCodec("libx264");
            _cmd.Crf(23);
            _cmd.Preset("medium");
            _cmd.AudioCodec("aac");
            _cmd.AddArgument("-b:a", "192k");
            _cmd.AddArgument("-movflags", "+faststart");
        }

        return this;
    }

    // ───────────────────────────────────────────────
    // Probe
    // ───────────────────────────────────────────────
    public async Task<MediaInfo> ProbeInputAsync(CancellationToken ct = default)
    {
        if (_inputPath is null)
            throw new InvalidOperationException("Input must be set using .From(...) before probing.");

        if (_probedInfo is not null)
            return _probedInfo;

        var ffprobeRunner = new FFprobeRunner(_runner, _options);

        try
        {
            string json = await ffprobeRunner.GetJsonOutputAsync(_inputPath, ct: ct);
            _probedInfo = MediaInfoParser.Parse(json);

            _options.Logger?.LogInformation(
                "Probed: {duration:F1}s | Video: {vcodec} {w}x{h} | Audio: {acodec}",
                _probedInfo.Duration.TotalSeconds,
                _probedInfo.VideoStream?.CodecName ?? "–",
                _probedInfo.VideoStream?.Width ?? 0,
                _probedInfo.VideoStream?.Height ?? 0,
                _probedInfo.AudioStream?.CodecName ?? "–");

            return _probedInfo;
        }
        catch (Exception ex)
        {
            _options.Logger?.LogWarning(ex, "Failed to probe input {Input}", _inputPath);
            throw;
        }
    }

    // ───────────────────────────────────────────────
    // Execute
    // ───────────────────────────────────────────────
    public async Task<FFmpegResult> ExecuteAsync(CancellationToken ct = default)
    {
        if (_inputPath is null)
            throw new InvalidOperationException("Input file must be set using .From(...)");

        if (_outputPath is null)
            throw new InvalidOperationException("Output file must be set using .To(...)");

        // ── Probe if needed ──
        TimeSpan? effectiveDuration = _knownDuration ?? _probedInfo?.Duration;
        if (_autoProbe && !effectiveDuration.HasValue)
        {
            try
            {
                var media = await ProbeInputAsync(ct);
                effectiveDuration = media.Duration;
                _knownDuration = media.Duration;   // cache it
            }
            catch
            {
                _options.Logger?.LogWarning("Could not determine input duration.");
            }
        }

        // Enable structured progress
        _cmd.WithProgressUrl("pipe:1");
        _cmd.WithNoStats(true);

        var args = _cmd.Build();

        _options.Logger?.LogDebug("FFmpeg command: {cmd}", _cmd.BuildAsString());
        Console.WriteLine("Command: " + _cmd.BuildAsString());   // For debugging

        var stopwatch = Stopwatch.StartNew();
        FFmpegProgress? finalProgress = null;

        var progressWrapper = new Progress<FFmpegProgress>(p =>
        {
            _lastProgress = p;
            finalProgress = p;
            _externalProgress?.Report(p);
        });

        try
        {
            var processResult = await _runner.RunFFmpegAsync(
                arguments: args,
                options: _options,
                progress: progressWrapper,
                knownDuration: effectiveDuration,
                cancellationToken: ct);

            stopwatch.Stop();

            if (processResult.Success)
            {
                return FFmpegResult.SuccessResult(_outputPath, finalProgress, processResult);
            }

            return FFmpegResult.FailureFromProcess(processResult, finalProgress, _outputPath);
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

    public string GetCommandLine() => _cmd.BuildAsString();
}