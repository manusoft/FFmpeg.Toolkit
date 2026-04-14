using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Core;
using ManuHub.FF.NET.Models;
using ManuHub.FF.NET.Parsing;
using ManuHub.FF.NET.Utils;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ManuHub.FF.NET.Builders;

/// <summary>
/// Fluent builder for concatenating multiple video/audio files using FFmpeg.
/// Supports fast stream-copy when possible, fallback to re-encoding, and progress reporting.
/// </summary>
public class ConcatBuilder
{
    private readonly IFFmpegRunner _runner;
    private readonly FFmpegOptions _options;
    private readonly FFmpegCommandBuilder _cmd;
    private readonly TempFileManager _tempManager;

    private readonly List<InputSegment> _inputs = new();
    private string? _outputPath;
    private bool _forceReencode;
    private bool _useFilterConcat;
    private IProgress<FFmpegProgress>? _progress;
    private FFmpegProgress? _lastProgress;

    private class InputSegment
    {
        public string FilePath { get; set; } = string.Empty;
        public TimeSpan? Seek { get; set; }
        public TimeSpan? Duration { get; set; }
        public MediaInfo? Probed { get; set; }
    }

    public ConcatBuilder(IFFmpegRunner runner, FFmpegOptions? options = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options?.Clone() ?? new FFmpegOptions();
        _cmd = new FFmpegCommandBuilder(_options);
        _tempManager = new TempFileManager(_options);
    }

    // ───────────────────────────────────────────────
    // Add inputs
    // ───────────────────────────────────────────────

    public ConcatBuilder Add(string filePath, TimeSpan? seek = null, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new ArgumentException($"File not found: {filePath}", nameof(filePath));

        _inputs.Add(new InputSegment
        {
            FilePath = filePath,
            Seek = seek,
            Duration = duration
        });

        return this;
    }

    public ConcatBuilder AddRange(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
            Add(path);
        return this;
    }

    // ───────────────────────────────────────────────
    // Output
    // ───────────────────────────────────────────────

    public ConcatBuilder To(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path required", nameof(outputPath));

        _outputPath = outputPath;
        return this;
    }

    // ───────────────────────────────────────────────
    // Options
    // ───────────────────────────────────────────────

    /// <summary>
    /// Force re-encoding even if stream copy is possible (useful for format normalization).
    /// </summary>
    public ConcatBuilder ForceReencode(bool force = true)
    {
        _forceReencode = force;
        return this;
    }

    /// <summary>
    /// Use filter_complex concat instead of concat demuxer (slower but more flexible).
    /// Auto-enabled if inputs have incompatible formats.
    /// </summary>
    public ConcatBuilder UseFilterConcat(bool use = true)
    {
        _useFilterConcat = use;
        return this;
    }

    public ConcatBuilder WithProgress(IProgress<FFmpegProgress> progress)
    {
        _progress = progress;
        return this;
    }

    // ───────────────────────────────────────────────
    // Probe all inputs (async)
    // ───────────────────────────────────────────────

    public async Task ProbeAllAsync(CancellationToken ct = default)
    {
        if (_inputs.Count == 0)
            throw new InvalidOperationException("No inputs added");

        var ffprobe = new FFprobeRunner(_runner, _options);

        foreach (var input in _inputs)
        {
            if (input.Probed != null) continue;

            string json = await ffprobe.GetJsonOutputAsync(input.FilePath, ct: ct);
            input.Probed = MediaInfoParser.Parse(json);

            if (input.Probed.Duration <= TimeSpan.Zero)
            {
                _options.Logger?.LogWarning("Duration unknown for {File}", input.FilePath);
            }
        }
    }

    // ───────────────────────────────────────────────
    // Execute
    // ───────────────────────────────────────────────

    public async Task<FFmpegResult> ExecuteAsync(CancellationToken ct = default)
    {
        if (_inputs.Count == 0)
            throw new InvalidOperationException("At least one input required (.Add(...))");

        if (_outputPath == null)
            throw new InvalidOperationException("Output required (.To(...))");

        // Probe if not already done
        await ProbeAllAsync(ct).ConfigureAwait(false);

        ConfigureConcatStrategy();

        var args = _cmd.Build();

        var stopwatch = Stopwatch.StartNew();
        FFmpegProgress? finalProgress = null;

        // Create progress reporter
        var progressWrapper = new Progress<FFmpegProgress>(p =>
        {
            finalProgress = p;
            _progress?.Report(p);
        });

        // Calculate total estimated duration correctly
        TimeSpan totalDuration = TimeSpan.Zero;
        foreach (var input in _inputs)
        {
            totalDuration += input.Duration ?? input.Probed?.Duration ?? TimeSpan.Zero;
        }

        var parser = new ProgressParser(totalDuration > TimeSpan.Zero ? totalDuration : null, progressWrapper);

        try
        {
            var processResult = await _runner.RunFFmpegAsync(
                arguments: args,
                options: _options,
                progress: progressWrapper,   // ← correct: IProgress<FFmpegProgress>
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

    private void ConfigureConcatStrategy()
    {
        bool canUseFastCopy = !_forceReencode && CanAllBeCopied();

        if (canUseFastCopy && !_useFilterConcat)
        {
            // Fast path: concat demuxer + stream copy
            ConfigureConcatDemuxer();
        }
        else
        {
            // Safe path: filter_complex concat (handles format differences)
            ConfigureFilterConcat();
        }
    }

    private bool CanAllBeCopied()
    {
        if (_inputs.Count < 2) return false;

        var first = _inputs[0].Probed;
        if (first == null) return false;

        string? firstVCodec = first.Streams?.FirstOrDefault(x => x.CodecType == "video")?.CodecName?.ToLowerInvariant();
        string? firstACodec = first.Streams?.FirstOrDefault(x => x.CodecType == "audio")?.CodecName?.ToLowerInvariant();

        foreach (var input in _inputs.Skip(1))
        {
            var probed = input.Probed;
            if (probed == null) return false;

            if (probed.Streams?.FirstOrDefault(x => x.CodecType == "video")?.CodecName?.ToLowerInvariant() != firstVCodec ||
                probed.Streams?.FirstOrDefault(x => x.CodecType == "audio")?.CodecName?.ToLowerInvariant() != firstACodec)
            {
                return false;
            }
        }

        return true;
    }

    private void ConfigureConcatDemuxer()
    {
        // Create concat list file
        var lines = new List<string>();
        foreach (var input in _inputs)
        {
            var safePath = input.FilePath.Replace(@"\", @"\\").Replace("'", @"\'");
            lines.Add($"file '{safePath}'");

            if (input.Seek.HasValue)
                lines.Add($"inpoint {input.Seek.Value.TotalSeconds:F3}");
            if (input.Duration.HasValue)
                lines.Add($"outpoint {input.Duration.Value.TotalSeconds:F3}");
        }

        string concatListPath = _tempManager.WriteTempTextFile(string.Join("\n", lines), ".txt");

        _cmd.AddArgument("-f", "concat");
        _cmd.AddArgument("-safe", "0"); // allow absolute paths
        _cmd.Input(concatListPath);
        _cmd.CopyAllStreams();
        _cmd.Output(_outputPath!);

        _options.Logger?.LogInformation("Using fast concat demuxer (stream copy)");
    }

    private void ConfigureFilterConcat()
    {
        // Complex filter: [0:v][0:a][1:v][1:a]... concat=n=inputs:v=1:a=1
        var inputs = new List<string>();
        var vInputs = new List<string>();
        var aInputs = new List<string>();

        for (int i = 0; i < _inputs.Count; i++)
        {
            var seg = _inputs[i];

            _cmd.Input(seg.FilePath);

            string label = $"[{i}:v][{i}:a]";
            if (seg.Seek.HasValue || seg.Duration.HasValue)
            {
                var trim = new List<string>();

                if (seg.Seek.HasValue)
                    trim.Add($"trim=start={seg.Seek.Value.TotalSeconds:F3}");
                if (seg.Duration.HasValue)
                    trim.Add($"duration={seg.Duration.Value.TotalSeconds:F3}");

                if (trim.Count > 0)
                {
                    label = $"[trim{i}]";
                    _cmd.AddArgument("-filter_complex", $"[{i}:v]{string.Join(",", trim)}[v{i}];[{i}:a]atrim=start={seg.Seek?.TotalSeconds ?? 0:F3}[a{i}]");
                    vInputs.Add($"[v{i}]");
                    aInputs.Add($"[a{i}]");
                    continue;
                }
            }

            vInputs.Add($"[{i}:v]");
            aInputs.Add($"[{i}:a]");
        }

        string filter = string.Join("", vInputs) + string.Join("", aInputs) +
                        $"concat=n={_inputs.Count}:v=1:a=1[outv][outa]";

        _cmd.AddArgument("-filter_complex", filter);
        _cmd.AddArgument("-map", "[outv]");
        _cmd.AddArgument("-map", "[outa]");

        // Default to safe re-encode
        _cmd.VideoCodec("libx264");
        _cmd.Crf(23);
        _cmd.Preset("medium");
        _cmd.AudioCodec("aac");
        _cmd.AddArgument("-b:a", "192k");

        _cmd.Output(_outputPath!);

        _options.Logger?.LogInformation("Using filter_complex concat (re-encoding)");
    }

    public string GetCommandPreview() => _cmd.BuildAsString();
}