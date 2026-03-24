using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Core;
using ManuHub.FF.NET.Models;
using ManuHub.FF.NET.Parsing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ManuHub.FF.NET.Builders;

/// <summary>
/// Fluent builder for adding image and/or text watermarks to video files.
/// Uses overlay and drawtext filters; attempts stream copy when feasible.
/// </summary>
public class WatermarkBuilder
{
    private readonly IFFmpegRunner _runner;
    private readonly FFmpegOptions _options;
    private readonly FFmpegCommandBuilder _cmd;

    private string? _inputPath;
    private string? _outputPath;
    private MediaInfo? _probedInfo;
    private readonly List<string> _filterParts = new();
    private IProgress<FFmpegProgress>? _progress;
    private FFmpegProgress? _lastProgress;

    public WatermarkBuilder(IFFmpegRunner runner, FFmpegOptions? options = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options?.Clone() ?? new FFmpegOptions();
        _cmd = new FFmpegCommandBuilder(_options);
    }

    // ───────────────────────────────────────────────
    // Input / Output
    // ───────────────────────────────────────────────

    public WatermarkBuilder From(string inputPath)
    {
        _inputPath = inputPath ?? throw new ArgumentNullException(nameof(inputPath));
        _cmd.Input(inputPath);
        return this;
    }

    public WatermarkBuilder To(string outputPath)
    {
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        return this;
    }

    // ───────────────────────────────────────────────
    // Image Watermark
    // ───────────────────────────────────────────────

    public WatermarkBuilder AddImageWatermark(
        string imagePath,
        WatermarkPosition position = WatermarkPosition.BottomRight,
        double opacity = 0.7,
        int scaleWidth = 150,
        double rotation = 0,
        int margin = 20)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Watermark image not found", imagePath);

        string posExpr = GetPositionExpression(position, margin);

        string overlayFilter = $"[1:v]format=yuva420p,geq=r='r(X,Y)':a='({opacity})*alpha(X,Y)'[wm];" +
                               $"[0:v][wm]overlay={posExpr}:enable='between(t,0,999999)'";

        if (scaleWidth > 0)
            overlayFilter = overlayFilter.Replace("[1:v]", $"[1:v]scale={scaleWidth}:-1[wmscaled];[wmscaled]");

        if (Math.Abs(rotation) > 0.001)
            overlayFilter = overlayFilter.Replace("[1:v]", $"[1:v]rotate={rotation * Math.PI / 180}:ow=rotw(iw):oh=roth(ih)[wmrot];[wmrot]");

        _filterParts.Add(overlayFilter);
        _cmd.Input(imagePath);  // second input = watermark image

        return this;
    }

    // ───────────────────────────────────────────────
    // Text Watermark
    // ───────────────────────────────────────────────

    public WatermarkBuilder AddTextWatermark(
        string text,
        WatermarkPosition position = WatermarkPosition.TopLeft,
        string fontFile = "arial.ttf",
        int fontSize = 24,
        string fontColor = "white",
        string borderColor = "black@0.5",
        int borderWidth = 2,
        double opacity = 1.0,
        int margin = 20)
    {
        string posExpr = GetPositionExpression(position, margin);

        string escapedText = text.Replace("'", "'\\''").Replace(":", "\\:");

        string drawtextFilter =
            $"drawtext=fontfile='{EscapeFilterPath(fontFile)}':text='{escapedText}':" +
            $"fontsize={fontSize}:fontcolor={fontColor}@={opacity}:borderw={borderWidth}:bordercolor={borderColor}:" +
            $"x={posExpr.Split(':')[0]}:y={posExpr.Split(':')[1]}:enable='between(t,0,999999)'";

        _filterParts.Add(drawtextFilter);

        return this;
    }

    // ───────────────────────────────────────────────
    // Positioning enum
    // ───────────────────────────────────────────────

    public enum WatermarkPosition
    {
        TopLeft, TopCenter, TopRight,
        MiddleLeft, Center, MiddleRight,
        BottomLeft, BottomCenter, BottomRight,
        Custom
    }

    // ───────────────────────────────────────────────
    // Progress
    // ───────────────────────────────────────────────

    public WatermarkBuilder WithProgress(IProgress<FFmpegProgress> progress)
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
        if (_inputPath == null || _outputPath == null)
            throw new InvalidOperationException("Input and output paths required");

        if (_filterParts.Count == 0)
            throw new InvalidOperationException("Add at least one watermark (.AddImageWatermark / .AddTextWatermark)");

        // Probe for duration (progress)
        await ProbeAsync(ct).ConfigureAwait(false);

        ConfigureWatermarkCommand();

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
                knownDuration: _probedInfo?.Duration,
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

    private void ConfigureWatermarkCommand()
    {
        // Watermark always requires re-encoding (filter changes pixels)
        _cmd.VideoCodec("libx264");
        _cmd.Crf(23);
        _cmd.Preset("medium");
        _cmd.CopyAudio();           // keep original audio if possible
        //_cmd.CopySubtitles();       // optional – can be overridden

        // Build complex filter chain
        string filterChain = string.Join(";", _filterParts);

        // If multiple filters, chain them properly
        if (_filterParts.Count > 1)
        {
            // last filter output → map
            filterChain += ";[out]";
            _cmd.AddArgument("-map", "[out]");
            _cmd.AddArgument("-map", "0:a?"); // audio from first input
        }

        _cmd.AddArgument("-filter_complex", filterChain);

        _cmd.Output(_outputPath!);

        _options.Logger?.LogInformation($"Applying {_filterParts.Count} watermark(s)");
    }

    private string GetPositionExpression(WatermarkPosition pos, int margin)
    {
        return pos switch
        {
            WatermarkPosition.TopLeft => $"x={margin}:y={margin}",
            WatermarkPosition.TopCenter => $"x=(main_w-overlay_w)/2:y={margin}",
            WatermarkPosition.TopRight => $"x=main_w-overlay_w-{margin}:y={margin}",
            WatermarkPosition.MiddleLeft => $"x={margin}:y=(main_h-overlay_h)/2",
            WatermarkPosition.Center => $"x=(main_w-overlay_w)/2:y=(main_h-overlay_h)/2",
            WatermarkPosition.MiddleRight => $"x=main_w-overlay_w-{margin}:y=(main_h-overlay_h)/2",
            WatermarkPosition.BottomLeft => $"x={margin}:y=main_h-overlay_h-{margin}",
            WatermarkPosition.BottomCenter => $"x=(main_w-overlay_w)/2:y=main_h-overlay_h-{margin}",
            WatermarkPosition.BottomRight => $"x=main_w-overlay_w-{margin}:y=main_h-overlay_h-{margin}",
            _ => "x=10:y=10" // fallback
        };
    }

    private static string EscapeFilterPath(string path)
    {
        return path.Replace(@"\", @"\\").Replace("'", @"\'").Replace(":", @"\:");
    }

    public string GetCommandPreview() => _cmd.BuildAsString();
}
