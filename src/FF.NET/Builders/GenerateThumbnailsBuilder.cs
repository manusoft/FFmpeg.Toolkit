using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Core;
using ManuHub.FF.NET.Models;
using ManuHub.FF.NET.Parsing;
using ManuHub.FF.NET.Utils;
using System.Diagnostics;

namespace ManuHub.FF.NET.Builders;

/// <summary>
/// Fluent builder for generating one or multiple video thumbnails using FFmpeg.
/// Supports single timestamp, regular intervals, evenly distributed thumbnails,
/// and optional tile/montage output.
/// </summary>
public class GenerateThumbnailsBuilder
{
    private readonly IFFmpegRunner _runner;
    private readonly FFmpegOptions _options;
    private readonly FFmpegCommandBuilder _cmd;
    private readonly TempFileManager _tempManager;

    private string? _inputPath;
    private string? _outputPattern;           // e.g. "thumb_%03d.png" or single file
    private string? _singleOutputPath;
    private MediaInfo? _probedInfo;
    private IProgress<FFmpegProgress>? _progress;
    private FFmpegProgress? _lastProgress;

    private TimeSpan? _atTime;
    private TimeSpan? _interval;
    private int? _count;
    private int? _tileRows;
    private int? _tileCols;
    private int _quality = 5;                 // CRF for JPG (lower = better), ignored for PNG
    private string _format = "png";           // png or jpg
    private int _width = 320;                 // thumbnail width (height auto)

    public GenerateThumbnailsBuilder(IFFmpegRunner runner, FFmpegOptions? options = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options?.Clone() ?? new FFmpegOptions();
        _cmd = new FFmpegCommandBuilder(_options);
        _tempManager = new TempFileManager(_options);
    }

    // ───────────────────────────────────────────────
    // Input / Output
    // ───────────────────────────────────────────────

    public GenerateThumbnailsBuilder From(string inputPath)
    {
        _inputPath = inputPath ?? throw new ArgumentNullException(nameof(inputPath));
        _cmd.Input(inputPath);
        return this;
    }

    /// <summary>
    /// Extract a single thumbnail and save it to this exact path.
    /// </summary>
    public GenerateThumbnailsBuilder To(string outputPath)
    {
        _singleOutputPath = outputPath;
        _outputPattern = null;
        return this;
    }

    /// <summary>
    /// Pattern for multiple thumbnails (must contain %d or %03d etc.)
    /// Example: "thumbs/frame_%04d.jpg"
    /// </summary>
    public GenerateThumbnailsBuilder OutputPattern(string pattern)
    {
        _outputPattern = pattern;
        _singleOutputPath = null;
        return this;
    }

    // ───────────────────────────────────────────────
    // Extraction modes (mutually exclusive)
    // ───────────────────────────────────────────────

    /// <summary>
    /// Extract thumbnail at exact timestamp
    /// </summary>
    public GenerateThumbnailsBuilder At(TimeSpan time)
    {
        _atTime = time;
        _interval = null;
        _count = null;
        return this;
    }

    /// <summary>
    /// Extract thumbnails every N seconds
    /// </summary>
    public GenerateThumbnailsBuilder Every(TimeSpan interval)
    {
        _interval = interval;
        _atTime = null;
        _count = null;
        return this;
    }

    /// <summary>
    /// Extract exactly N thumbnails evenly distributed across the video
    /// </summary>
    public GenerateThumbnailsBuilder Count(int numberOfThumbnails)
    {
        if (numberOfThumbnails < 1) throw new ArgumentOutOfRangeException(nameof(numberOfThumbnails));
        _count = numberOfThumbnails;
        _atTime = null;
        _interval = null;
        return this;
    }

    // ───────────────────────────────────────────────
    // Appearance / Quality
    // ───────────────────────────────────────────────

    public GenerateThumbnailsBuilder Width(int pixels)
    {
        _width = Math.Max(64, pixels);
        return this;
    }

    public GenerateThumbnailsBuilder Format(string format) // "png" or "jpg"
    {
        var f = format.ToLowerInvariant();
        if (f is not ("png" or "jpg" or "jpeg")) throw new ArgumentException("Supported: png, jpg");
        _format = f;
        return this;
    }

    public GenerateThumbnailsBuilder Quality(int quality) // 2–31 for jpg (lower=better), ignored for png
    {
        _quality = Math.Clamp(quality, 2, 31);
        return this;
    }

    // ───────────────────────────────────────────────
    // Montage / Tile output (all thumbs in one image)
    // ───────────────────────────────────────────────

    public GenerateThumbnailsBuilder Tile(int rows, int columns)
    {
        _tileRows = rows;
        _tileCols = columns;
        return this;
    }

    // ───────────────────────────────────────────────
    // Progress
    // ───────────────────────────────────────────────

    public GenerateThumbnailsBuilder WithProgress(IProgress<FFmpegProgress> progress)
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
            throw new InvalidOperationException("Input must be set first (.From(...))");

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

        if (_singleOutputPath == null && _outputPattern == null)
            throw new InvalidOperationException("Output required (.To(...) or .OutputPattern(...))");

        // Probe if not already done
        if (_probedInfo == null)
        {
            await ProbeAsync(ct).ConfigureAwait(false);
        }

        if (_probedInfo!.Duration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Cannot generate thumbnails: duration unknown or zero.");
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
        // Common settings
        _cmd.CopyVideo();           // we only need one frame → copy is fastest
        _cmd.AddArgument("-an");    // no audio
        _cmd.AddArgument("-sn");    // no subtitles
        _cmd.AddArgument("-frames:v", "1"); // one frame per output (except montage)

        // Size
        _cmd.Scale(_width, null);   // height auto

        // Quality / format
        if (_format == "jpg")
        {
            _cmd.VideoCodec("mjpeg");
            _cmd.Crf(_quality);
        }
        else
        {
            _cmd.VideoCodec("png");
        }

        // ── Extraction mode ────────────────────────────────────────

        if (_atTime.HasValue)
        {
            // Single at exact time
            _cmd.SeekInput(_atTime.Value);
            _cmd.Output(GetSingleOrPatternPath(1));
        }
        else if (_interval.HasValue)
        {
            // Every N seconds
            _cmd.AddArgument("-vf", $"fps=1/{_interval.Value.TotalSeconds:F3}");
            _cmd.Output(GetPatternPath());
        }
        else if (_count.HasValue)
        {
            // Evenly distributed
            double totalSec = _probedInfo!.Duration.TotalSeconds;
            double step = totalSec / (_count.Value + 1); // avoid start & very end

            var times = Enumerable.Range(1, _count.Value)
                .Select(i => TimeSpan.FromSeconds(i * step));

            if (_tileRows.HasValue && _tileCols.HasValue)
            {
                // Montage mode: generate all then tile in one command
                var filterParts = new List<string>();

                for (int i = 0; i < _count.Value; i++)
                {
                    var t = times.ElementAt(i);
                    filterParts.Add($"[0:v]trim=start={t.TotalSeconds:F3},setpts=PTS-STARTPTS,scale={_width}:-1[s{i}];");
                }

                var overlayChain = string.Join("", Enumerable.Range(0, _count.Value).Select(i => $"[s{i}]"));
                int cols = _tileCols.Value;
                int rows = _tileRows.Value;

                filterParts.Add($"{overlayChain}tile={cols}x{rows}[out]");

                _cmd.AddArgument("-filter_complex", string.Join("", filterParts));
                _cmd.AddArgument("-map", "[out]");
                _cmd.Output(_singleOutputPath ?? _tempManager.CreateTempFile("." + _format));
            }
            else
            {
                // Multiple separate files
                _cmd.AddArgument("-vf", string.Join(",", times.Select(t => $"select=eq(n\\,0)+gte(t\\,{t.TotalSeconds:F3})")));
                _cmd.Output(GetPatternPath());
            }
        }
        else
        {
            // Default: middle frame
            _atTime = _probedInfo!.Duration / 2;
            _cmd.SeekInput(_atTime.Value);
            _cmd.Output(GetSingleOrPatternPath(1));
        }
    }

    private string GetSingleOrPatternPath(int index = 1)
    {
        if (_singleOutputPath != null) return _singleOutputPath;
        return GetPatternPath().Replace("%d", index.ToString());
    }

    private string GetPatternPath()
    {
        if (_outputPattern != null) return _outputPattern;

        // Default pattern in temp folder
        string ext = _format == "jpg" ? ".jpg" : ".png";
        return Path.Combine(_options.TempDirectory, "thumb_%04d" + ext);
    }

    private string? GetPrimaryOutputPath()
    {
        if (_singleOutputPath != null) return _singleOutputPath;
        if (_outputPattern != null) return _outputPattern.Replace("%d", "0001").Replace("%03d", "001");
        return null;
    }
}