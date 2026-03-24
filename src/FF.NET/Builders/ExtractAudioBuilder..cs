using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Core;
using ManuHub.FF.NET.Models;
using ManuHub.FF.NET.Parsing;
using ManuHub.FF.NET.Utils;
using System.Diagnostics;

namespace ManuHub.FF.NET.Builders;

/// <summary>
/// Fluent builder for extracting audio from video or audio files using FFmpeg.
/// Supports selecting specific audio streams, format conversion, bitrate control,
/// and basic trimming.
/// </summary>
public class ExtractAudioBuilder
{
    private readonly IFFmpegRunner _runner;
    private readonly FFmpegOptions _options;
    private readonly FFmpegCommandBuilder _cmd;
    private readonly TempFileManager _tempManager;

    private string? _inputPath;
    private string? _outputPath;
    private MediaInfo? _probedInfo;
    private int _audioStreamIndex = -1;       // -1 = auto (usually first/default)
    private string _outputFormat = "mp3";     // mp3, aac, wav, opus, flac, m4a, ...
    private int? _bitrateKbps;
    private TimeSpan? _seek;
    private TimeSpan? _duration;
    private IProgress<FFmpegProgress>? _progress;
    private FFmpegProgress? _lastProgress;

    public ExtractAudioBuilder(IFFmpegRunner runner, FFmpegOptions? options = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options?.Clone() ?? new FFmpegOptions();
        _cmd = new FFmpegCommandBuilder(_options);
        _tempManager = new TempFileManager(_options);
    }

    // ───────────────────────────────────────────────
    // Input / Output
    // ───────────────────────────────────────────────

    public ExtractAudioBuilder From(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path required", nameof(inputPath));

        _inputPath = inputPath;
        _cmd.Input(inputPath);
        return this;
    }

    public ExtractAudioBuilder To(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path required", nameof(outputPath));

        _outputPath = outputPath;
        return this;
    }

    // ───────────────────────────────────────────────
    // Audio stream selection
    // ───────────────────────────────────────────────

    /// <summary>
    /// Select specific audio stream by index (0-based).
    /// Use -1 (default) for automatic/first/default stream.
    /// </summary>
    public ExtractAudioBuilder AudioStream(int streamIndex)
    {
        _audioStreamIndex = streamIndex >= -1 ? streamIndex : -1;
        return this;
    }

    // ───────────────────────────────────────────────
    // Output format & quality
    // ───────────────────────────────────────────────

    public ExtractAudioBuilder Format(string format)
    {
        var f = format.ToLowerInvariant().TrimStart('.');
        if (!IsSupportedAudioFormat(f))
            throw new ArgumentException($"Unsupported audio format: {format}. Supported: mp3, aac, m4a, wav, opus, flac");

        _outputFormat = f;
        return this;
    }

    public ExtractAudioBuilder Bitrate(int kbps)
    {
        if (kbps < 32 || kbps > 512)
            throw new ArgumentOutOfRangeException(nameof(kbps), "Reasonable range: 32–512 kbps");

        _bitrateKbps = kbps;
        return this;
    }

    // ───────────────────────────────────────────────
    // Trimming / Seeking
    // ───────────────────────────────────────────────

    public ExtractAudioBuilder Seek(TimeSpan start)
    {
        _seek = start >= TimeSpan.Zero ? start : null;
        return this;
    }

    public ExtractAudioBuilder Duration(TimeSpan length)
    {
        _duration = length > TimeSpan.Zero ? length : null;
        return this;
    }

    public ExtractAudioBuilder Trim(TimeSpan start, TimeSpan length)
    {
        _seek = start;
        _duration = length;
        return this;
    }

    // ───────────────────────────────────────────────
    // Progress
    // ───────────────────────────────────────────────

    public ExtractAudioBuilder WithProgress(IProgress<FFmpegProgress> progress)
    {
        _progress = progress;
        return this;
    }

    // ───────────────────────────────────────────────
    // Probe helper
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

        // Probe if not already done (helps with duration for progress)
        if (_probedInfo == null)
        {
            await ProbeAsync(ct).ConfigureAwait(false);
        }

        ConfigureCommand();

        var args = _cmd.Build();

        var stopwatch = Stopwatch.StartNew();
        FFmpegProgress? finalProgress = null;

        var progressWrapper = new Progress<FFmpegProgress>(p =>
        {
            finalProgress = p;
            _progress?.Report(p);
        });

        var parser = new ProgressParser(_probedInfo?.Duration, progressWrapper);

        try
        {
            var processResult = await _runner.RunFFmpegAsync(
                args,
                _options,
                progressWrapper,
                knownDuration:_probedInfo?.Duration,
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

    private void ConfigureCommand()
    {
        // Disable video & subtitles
        _cmd.AddArgument("-vn");   // no video
        _cmd.AddArgument("-sn");   // no subtitles

        // Stream selection
        if (_audioStreamIndex >= 0)
        {
            _cmd.AddArgument("-map", $"0:a:{_audioStreamIndex}");
        }
        else
        {
            _cmd.AddArgument("-map", "0:a?");   // first audio stream if available
        }

        // Seeking & duration
        if (_seek.HasValue)
            _cmd.SeekInput(_seek.Value);

        if (_duration.HasValue)
            _cmd.Duration(_duration.Value);

        // Codec & format
        string codec = GetAudioCodecForFormat(_outputFormat);
        _cmd.AudioCodec(codec);

        // Bitrate / quality
        if (_bitrateKbps.HasValue)
        {
            _cmd.AddArgument("-b:a", $"{_bitrateKbps.Value}k");
        }
        else if (_outputFormat is "mp3" or "aac" or "m4a")
        {
            // sensible default if not specified
            _cmd.AddArgument("-b:a", "192k");
        }

        // Container / extension handling is mostly done by output filename
        // but we can force some flags
        if (_outputFormat == "mp3")
        {
            _cmd.AddArgument("-id3v2_version", "3"); // better metadata compatibility
        }

        _cmd.Output(_outputPath!);
    }

    private static bool IsSupportedAudioFormat(string format)
    {
        return format is "mp3" or "aac" or "m4a" or "wav" or "opus" or "flac" or "ogg";
    }

    private static string GetAudioCodecForFormat(string format)
    {
        return format switch
        {
            "mp3" => "libmp3lame",
            "aac" => "aac",
            "m4a" => "aac",
            "wav" => "pcm_s16le",
            "opus" => "libopus",
            "flac" => "flac",
            "ogg" => "libvorbis",
            _ => "copy"   // fallback – try stream copy if format matches
        };
    }

    // Helper: get command line for debugging
    public string GetCommandLinePreview() => _cmd.BuildAsString();
}