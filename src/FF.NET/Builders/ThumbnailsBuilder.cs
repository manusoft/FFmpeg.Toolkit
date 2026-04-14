using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Core;
using ManuHub.FF.NET.Models;
using ManuHub.FF.NET.Parsing;
using ManuHub.FF.NET.Utils;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace ManuHub.FF.NET.Builders;

/// <summary>
/// Fluent builder for generating one or multiple video thumbnails using FFmpeg.
/// Supports single timestamp, regular intervals, evenly distributed thumbnails,
/// and optional tile/montage output.
/// </summary>
public class ThumbnailsBuilder
{
    private readonly IFFmpegRunner _runner;
    private readonly FFmpegOptions _options;
    private readonly FFmpegCommandBuilder _cmd;
    private readonly TempFileManager _tempManager;

    private string? _input;
    private string? _pattern;
    private string? _single;

    private MediaInfo? _probedInfo;

    private IProgress<FFmpegProgress>? _progress;
    private FFmpegProgress? _lastProgress;

    private TimeSpan? _at;
    private TimeSpan? _interval;
    private int? _count;

    private bool _sceneMode;
    private double _sceneThreshold = 0.4;

    private bool _keyframesOnly;

    private int? _tileRows;
    private int? _tileCols;

    private int _width = 320;
    private string _format = "jpg";
    private int _quality = 5;

    public ThumbnailsBuilder(IFFmpegRunner runner, FFmpegOptions? options = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options?.Clone() ?? new FFmpegOptions();
        _cmd = new FFmpegCommandBuilder(_options);
        _tempManager = new TempFileManager(_options);
    }

    // ───────────────────────────────────────────────
    // Input / Output
    // ───────────────────────────────────────────────

    public ThumbnailsBuilder From(string inputPath)
    {
        _input = inputPath ?? throw new ArgumentNullException(nameof(inputPath));
        _cmd.Input(inputPath);
        return this;
    }

    /// <summary>
    /// Extract a single thumbnail and save it to this exact path.
    /// </summary>
    public ThumbnailsBuilder To(string outputPath)
    {
        _single = outputPath;
        _pattern = null;
        return this;
    }

    /// <summary>
    /// Pattern for multiple thumbnails (must contain %d or %03d etc.)
    /// Example: "thumbs/frame_%04d.jpg"
    /// </summary>
    public ThumbnailsBuilder OutputPattern(string pattern)
    {
        if (!pattern.Contains("%"))
            throw new ArgumentException("Pattern must contain %d or %03d");

        _pattern = pattern;
        _single = null;
        return this;
    }

    // ───────────────────────────────────────────────
    // Extraction modes (mutually exclusive)
    // ───────────────────────────────────────────────

    /// <summary>
    /// Extract thumbnail at exact timestamp
    /// </summary>
    public ThumbnailsBuilder At(TimeSpan time)
    {
        ResetModes();
        _at = time;
        return this;
    }

    /// <summary>
    /// Extract thumbnails every N seconds
    /// </summary>
    public ThumbnailsBuilder Every(TimeSpan interval)
    {
        ResetModes();
        _interval = interval;
        return this;
    }

    /// <summary>
    /// Extract exactly N thumbnails evenly distributed across the video
    /// </summary>
    public ThumbnailsBuilder Count(int numberOfThumbnails)
    {
        if (numberOfThumbnails < 1) throw new ArgumentOutOfRangeException(nameof(numberOfThumbnails));

        ResetModes();
        _count = numberOfThumbnails;
        return this;
    }

    public ThumbnailsBuilder Scene(double threshold = 0.4)
    {
        ResetModes();
        _sceneMode = true;
        _sceneThreshold = threshold;
        return this;
    }

    public ThumbnailsBuilder KeyframesOnly()
    {
        _keyframesOnly = true;
        return this;
    }

    // ───────────────────────────────────────────────
    // Montage / Tile output (all thumbs in one image)
    // ───────────────────────────────────────────────

    public ThumbnailsBuilder Tile(int rows, int columns)
    {
        _tileRows = rows;
        _tileCols = columns;
        return this;
    }

    private void ResetModes()
    {
        _at = null;
        _interval = null;
        _count = null;
        _sceneMode = false;
    }


    // ───────────────────────────────────────────────
    // Appearance / Quality
    // ───────────────────────────────────────────────

    public ThumbnailsBuilder Width(int pixels)
    {
        _width = Math.Max(64, pixels);
        return this;
    }

    public ThumbnailsBuilder Format(string format) // "png" or "jpg"
    {
        var f = format.ToLowerInvariant();
        if (f is not ("png" or "jpg" or "jpeg")) throw new ArgumentException("Supported: png, jpg");
        _format = f;
        return this;
    }

    public ThumbnailsBuilder Quality(int quality) // 2–31 for jpg (lower=better), ignored for png
    {
        _quality = Math.Clamp(quality, 2, 31);
        return this;
    }
       
    // ───────────────────────────────────────────────
    // Progress
    // ───────────────────────────────────────────────
    public ThumbnailsBuilder WithProgress(IProgress<FFmpegProgress> progress)
    {
        _progress = progress;
        return this;
    }

    // ───────────────────────────────────────────────
    // Execute
    // ───────────────────────────────────────────────
    public async Task<FFmpegResult> ExecuteAsync(CancellationToken ct = default)
    {
        if (_input == null)
            throw new InvalidOperationException("Input required (.From(...))");

        if (_single == null && _pattern == null)
            throw new InvalidOperationException("Output required (.To(...) or .OutputPattern(...))");

        // Probe if not already done
        if (_probedInfo == null)
            await ProbeAsync(ct);

        if (_probedInfo!.Duration <= TimeSpan.Zero)
            throw new InvalidOperationException("Cannot generate thumbnails: duration unknown or zero.");

        ConfigureCommand();

        var args = _cmd.Build();

        var stopwatch = Stopwatch.StartNew();
        FFmpegProgress? finalProgress = null;

        var progressWrapper = new Progress<FFmpegProgress>(p =>
        {
            finalProgress = p;
            _progress?.Report(p);
        });

        var parser = new ProgressParser(_probedInfo.Duration, progressWrapper);

        try
        {
            var processResult = await _runner.RunFFmpegAsync(
                args,
                _options,
                progressWrapper,
                knownDuration: _probedInfo.Duration,
                ct);

            stopwatch.Stop();

            return processResult.Success
                ? FFmpegResult.SuccessResult(GetPrimaryOutputPath(), finalProgress, processResult)
                : FFmpegResult.FailureFromProcess(processResult, finalProgress, GetPrimaryOutputPath());
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            return FFmpegResult.CancelledResult(stopwatch.Elapsed, finalProgress, ex);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return FFmpegResult.FromException(ex, GetPrimaryOutputPath(), finalProgress);
        }
    }

    private void ConfigureCommand()
    {
        _cmd.AddArgument("-an");
        _cmd.AddArgument("-sn");

        var filters = new List<string>();

        // ───── MODE FILTERS ─────

        if (_interval.HasValue)
        {
            double fps = 1.0 / _interval.Value.TotalSeconds;
            filters.Add($"fps={fps:F6}");
        }
        else if (_count.HasValue)
        {
            double fps = _count.Value / _probedInfo!.Duration.TotalSeconds;
            filters.Add($"fps={fps:F6}");
        }
        else if (_sceneMode)
        {
            filters.Add($"select='gt(scene,{_sceneThreshold})'");
        }

        if (_keyframesOnly)
        {
            filters.Add("select='eq(pict_type,I)'");
        }

        // scale always last
        filters.Add($"scale={_width}:-1");

        // ───── TILE ─────

        if (_tileRows.HasValue && _tileCols.HasValue)
        {
            filters.Add($"tile={_tileCols}x{_tileRows}");
            _cmd.AddArgument("-frames:v", "1");
        }

        // apply filters
        _cmd.AddArgument("-vf", string.Join(",", filters));

        // codec
        if (_format is "jpg" or "jpeg")
        {
            _cmd.AddArgument("-c:v", "mjpeg");
            _cmd.AddArgument("-q:v", _quality.ToString());
        }
        else
        {
            _cmd.AddArgument("-c:v", "png");
        }

        // ───── SINGLE MODE ─────

        if (_at.HasValue)
        {
            _cmd.SeekInput(_at.Value);
            _cmd.AddArgument("-frames:v", "1");
            _cmd.Output(_single ?? Replace(_pattern!, 1));
            return;
        }

        // ───── OUTPUT ─────

        if (_tileRows.HasValue)
        {
            _cmd.Output(_single ?? Path.Combine(_options.TempDirectory, "tile." + _format));
        }
        else
        {
            _cmd.Output(_pattern!);
        }
    }

    // ───────── HELPERS ─────────
    private static string Replace(string pattern, int i)
    {
        return pattern.Replace("%04d", i.ToString("D4"))
                      .Replace("%03d", i.ToString("D3"))
                      .Replace("%d", i.ToString());
    }

    private string? GetPrimaryOutputPath()
    {
        if (_single != null) return _single;
        if (_pattern != null) return Replace(_pattern, 1);
        return null;
    }

    // ───────────────────────────────────────────────
    // Probe helper
    // ───────────────────────────────────────────────
    private async Task<MediaInfo?> ProbeAsync(CancellationToken ct = default)
    {
        if (_probedInfo is not null)
            return _probedInfo;

        var ffprobeRunner = new FFprobeRunner(_runner, _options);

        try
        {
            string json = await ffprobeRunner.GetJsonOutputAsync(_input, ct: ct);
            _probedInfo = JsonSerializer.Deserialize<MediaInfo>(json);
            if (_probedInfo == null) return null;

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
            _options.Logger?.LogWarning(ex, "Failed to probe input {Input}", _input);
            throw;
        }
    }
}