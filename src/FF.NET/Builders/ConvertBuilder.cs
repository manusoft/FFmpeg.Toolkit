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
public class ConvertBuilder
{
    private readonly IFFmpegRunner _runner;
    private readonly FFmpegOptions _options;
    private readonly FFmpegCommandBuilder _cmd;

    private string? _inputPath;
    private string? _outputPath;
    private TimeSpan? _knownDuration; // Set this via .WithKnownDuration() if you have ffprobe info

    private MediaInfo? _probedInfo;
    public MediaInfo? ProbedInfo => _probedInfo;  // read-only public access

    private bool _autoProbe = true;               // default: probe if needed

    private IProgress<FFmpegProgress>? _externalProgress;
    private FFmpegProgress? _lastProgress;

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
    // Seeking / Duration control
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
    // Video settings
    // ───────────────────────────────────────────────

    public ConvertBuilder VideoCodec(string codec) { _cmd.VideoCodec(codec); return this; }
    public ConvertBuilder CopyVideo() { _cmd.CopyVideo(); return this; }
    public ConvertBuilder Crf(int crf) { _cmd.Crf(crf); return this; }
    public ConvertBuilder Preset(string preset) { _cmd.Preset(preset); return this; }
    public ConvertBuilder Scale(int width, int? height = null) { _cmd.Scale(width, height); return this; }
    public ConvertBuilder Fps(double fps) { _cmd.Fps(fps); return this; }
    public ConvertBuilder TwoPass(bool enable = true)
    {
        if (enable)
        {
            // Note: real two-pass needs two runs → this is just flag; full two-pass needs separate logic
            _cmd.AddArgument("-pass", "1"); // or handle in advanced builder
        }
        return this;
    }

    // ───────────────────────────────────────────────
    // Audio settings
    // ───────────────────────────────────────────────

    public ConvertBuilder AudioCodec(string codec) { _cmd.AudioCodec(codec); return this; }
    public ConvertBuilder CopyAudio() { _cmd.CopyAudio(); return this; }
    public ConvertBuilder AudioBitrate(int kbps)
    {
        _cmd.AddArgument("-b:a", $"{kbps}k");
        return this;
    }

    // ───────────────────────────────────────────────
    // Subtitles / Filters / Metadata
    // ───────────────────────────────────────────────

    public ConvertBuilder BurnSubtitles(string subtitlePath)
    {
        _cmd.BurnSubtitles(subtitlePath);
        return this;
    }

    public ConvertBuilder CopySubtitles()
    {
        _cmd.AddArgument("-c:s", "mov_text"); // or "copy" depending on container
        return this;
    }

    public ConvertBuilder Metadata(string key, string value)
    {
        _cmd.Metadata(key, value);
        return this;
    }

    // ───────────────────────────────────────────────
    // Progress & Duration info
    // ───────────────────────────────────────────────

    public ConvertBuilder WithProgress(IProgress<FFmpegProgress> progress)
    {
        _externalProgress = progress;
        return this;
    }

    /// <summary>
    /// Provide total input duration (from ffprobe) to enable accurate % and ETA.
    /// </summary>
    public ConvertBuilder WithKnownDuration(TimeSpan duration)
    {
        _knownDuration = duration;
        return this;
    }

    // ───────────────────────────────────────────────
    // Execution (this is where the pattern lives)
    // ───────────────────────────────────────────────

    public async Task<FFmpegResult> ExecuteAsync(CancellationToken ct = default)
    {
        if (_inputPath is null)
            throw new InvalidOperationException("Input file must be set using .From(...)");

        if (_outputPath is null)
            throw new InvalidOperationException("Output file must be set using .To(...)");

        // ───────────────────────────────────────────────
        // Inject -progress pipe:1 before output
        // ───────────────────────────────────────────────
        _cmd.AddArgument("-progress", "pipe:1");

        var args = _cmd.Build();

        Console.WriteLine("Running FFmpeg:");
        Console.WriteLine(string.Join(" ", args));

        var stopwatch = Stopwatch.StartNew();
        FFmpegProgress? finalProgress = null;

        // ───────────────────────────────────────────────
        // Probe if enabled and duration not already known
        // ───────────────────────────────────────────────
        TimeSpan? effectiveDuration = _knownDuration;

        if (_autoProbe && !effectiveDuration.HasValue)
        {
            try
            {
                var media = await ProbeInputAsync(ct).ConfigureAwait(false);
                if (media.Duration > TimeSpan.Zero)
                {
                    effectiveDuration = media.Duration;
                    _knownDuration = media.Duration;
                }
            }
            catch
            {
                _options.Logger?.LogWarning("Could not determine input duration for progress calculation.");
            }
        }

        // ───────────────────────────────────────────────
        // Setup ProgressParser & wrapper
        // ───────────────────────────────────────────────
        var progressWrapper = new Progress<FFmpegProgress>(p =>
        {
            finalProgress = p;
            _externalProgress?.Report(p);
        });

        var progressParser = new ProgressParser(effectiveDuration, progressWrapper);

        // Feed progress lines from stderr (or better: use -progress pipe:1 if your FFmpegProcessRunner supports it)
        try
        {
            var processResult = await _runner.RunFFmpegAsync(
                arguments: args,
                options: _options,
                progress: progressWrapper,
                cancellationToken: ct);

            stopwatch.Stop();

            if (processResult.Success)
            {
                return FFmpegResult.SuccessResult(
                    outputFilePath: _outputPath,
                    finalProgress: finalProgress,
                    processResult: processResult);
            }

            return FFmpegResult.FailureFromProcess(
                processResult,
                finalProgress,
                _outputPath);
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            return FFmpegResult.CancelledResult(
                durationSoFar: stopwatch.Elapsed,
                lastProgress: finalProgress,
                exception: ex);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return FFmpegResult.FromException(
                ex,
                _outputPath,
                finalProgress);
        }
    }

    // Helper: get generated command for debugging / logging
    public string GetCommandLine() => _cmd.BuildAsString();


    // PROBE
    public ConvertBuilder AutoProbe(bool enable = true)
    {
        _autoProbe = enable;
        return this;
    }

    public ConvertBuilder ProbeBeforeExecute()
    {
        _autoProbe = true;
        return this;
    }

    /// <summary>
    /// After probing, automatically decide whether to stream-copy compatible streams
    /// (fast, no quality loss) or fall back to re-encoding.
    /// 
    /// Best used for MP4 output when you want maximal compatibility + speed.
    /// Call this after .From(...) and before .ExecuteAsync().
    /// </summary>
    /// <param name="forceReencode">If true, always re-encode even if copy is possible</param>
    /// <returns>this</returns>
    public ConvertBuilder SmartCopyIfPossible(bool forceReencode = false)
    {
        if (_probedInfo == null)
        {
            throw new InvalidOperationException(
                "Input must be probed first. Call .ProbeInputAsync() or let auto-probe run.");
        }

        if (forceReencode)
        {
            // User explicitly wants re-encoding → do nothing here
            return this;
        }

        // Assume output is .mp4 (common case) – we can make this smarter later
        bool isMp4Output = _outputPath?.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) == true;
        if (!isMp4Output)
        {
            // For non-MP4 → safer to re-encode by default (can be extended)
            return this;
        }

        bool canCopyVideo = false;
        bool canCopyAudio = false;

        var video = _probedInfo.VideoStream;
        var audio = _probedInfo.AudioStream;

        // Video compatibility check for MP4
        if (video != null)
        {
            string vCodec = video.CodecName.ToLowerInvariant();

            bool isH264 = vCodec is "h264" or "avc" or "avc1";
            bool safePixelFormat = video.PixelFormat?.ToLowerInvariant() is "yuv420p" or null;

            canCopyVideo = isH264 && safePixelFormat;

            if (!canCopyVideo)
            {
                _options.Logger?.LogInformation(
                    "Video stream ({codec}, {pixFmt}) not ideal for direct MP4 copy → will re-encode",
                    vCodec, video.PixelFormat ?? "unknown");
            }
        }
        else
        {
            // No video → probably audio-only → copy audio
            canCopyVideo = true; // irrelevant
        }

        // Audio compatibility check for MP4
        if (audio != null)
        {
            string aCodec = audio.CodecName.ToLowerInvariant();

            bool isAac = aCodec is "aac" or "mp4a";

            canCopyAudio = isAac;

            if (!canCopyAudio)
            {
                _options.Logger?.LogInformation(
                    "Audio stream ({codec}) not AAC → will re-encode to AAC", aCodec);
            }
        }
        else
        {
            canCopyAudio = true; // no audio → fine
        }

        // Apply copy where possible
        if (canCopyVideo && canCopyAudio)
        {
            // Full copy – fastest, no quality loss
            _cmd.CopyAllStreams();
            _cmd.AddArgument("-movflags", "+faststart"); // excellent for web/mp4
            _options.Logger?.LogInformation("SmartCopy: using stream copy (-c copy + faststart)");
        }
        else if (canCopyVideo)
        {
            // Copy video, re-encode audio to AAC (common pattern)
            _cmd.CopyVideo();
            _cmd.AudioCodec("aac");
            _cmd.AddArgument("-b:a", "192k"); // reasonable default; can be overridden later
            _cmd.AddArgument("-movflags", "+faststart");
            _options.Logger?.LogInformation("SmartCopy: copying video, re-encoding audio to AAC");
        }
        else if (canCopyAudio)
        {
            // Rare: copy audio, re-encode video (e.g. old codec)
            _cmd.CopyAudio();
            _cmd.VideoCodec("libx264");
            _cmd.Crf(23);           // sensible default
            _cmd.Preset("medium");
            _cmd.AddArgument("-movflags", "+faststart");
            _options.Logger?.LogInformation("SmartCopy: copying audio, re-encoding video to H.264");
        }
        else
        {
            // Nothing copyable → default to safe re-encode
            _cmd.VideoCodec("libx264");
            _cmd.Crf(23);
            _cmd.Preset("medium");
            _cmd.AudioCodec("aac");
            _cmd.AddArgument("-b:a", "192k");
            _cmd.AddArgument("-movflags", "+faststart");
            _options.Logger?.LogInformation("SmartCopy: no streams copyable → full re-encode to H.264/AAC");
        }

        return this;
    }

    public async Task<MediaInfo> ProbeInputAsync(CancellationToken ct = default)
    {
        if (_inputPath is null)
            throw new InvalidOperationException("Input must be set using .From(...) before probing.");

        if (_probedInfo is not null)
            return _probedInfo; // already probed → cache hit

        var ffprobeRunner = new FFprobeRunner(_runner, _options);

        try
        {
            string json = await ffprobeRunner.GetJsonOutputAsync(_inputPath, ct: ct);

            _probedInfo = MediaInfoParser.Parse(json);

            // Optional: log some basics
            _options.Logger?.LogInformation(
                "Probed input: {duration}s | {videoCodec} {width}x{height} | {audioCodec}",
                _probedInfo.Duration.TotalSeconds.ToString("F1"),
                _probedInfo.VideoStream?.CodecName ?? "–",
                _probedInfo.VideoStream?.Width ?? 0,
                _probedInfo.VideoStream?.Height ?? 0,
                _probedInfo.AudioStream?.CodecName ?? "–");

            return _probedInfo;
        }
        catch (Exception ex)
        {
            _options.Logger?.LogWarning(ex, "Failed to probe input file {Input}", _inputPath);
            throw;
        }
    }
}