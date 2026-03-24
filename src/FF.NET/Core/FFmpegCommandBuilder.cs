using System.Globalization;
using System.Text;

namespace ManuHub.FF.NET.Core;

/// </summary>
public class FFmpegCommandBuilder
{
    private readonly List<string> _arguments = new();
    private readonly FFmpegOptions _options;

    private string? _inputFile;
    private string? _outputFile;
    private readonly List<string> _customPreInputs = new();   // -ss before -i
    private readonly List<string> _customPostInputs = new();  // after -i, before output
    private readonly List<string> _filters = new();
    private readonly List<string> _metadata = new();
    private bool _overwrite = true;
    private bool _hideBanner = true;
    private bool _nostats = false;

    public FFmpegCommandBuilder(FFmpegOptions? options = null)
    {
        _options = options ?? new FFmpegOptions();
    }

    // ───────────────────────────────────────────────
    // Basic input / output
    // ───────────────────────────────────────────────

    public FFmpegCommandBuilder Input(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path cannot be empty", nameof(inputPath));

        _inputFile = inputPath;
        return this;
    }

    public FFmpegCommandBuilder Output(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path cannot be empty", nameof(outputPath));

        _outputFile = outputPath;
        return this;
    }

    public FFmpegCommandBuilder OverwriteOutput(bool overwrite = true)
    {
        _overwrite = overwrite;
        return this;
    }

    // ───────────────────────────────────────────────
    // Common global flags
    // ───────────────────────────────────────────────

    public FFmpegCommandBuilder WithHideBanner(bool hide = true)
    {
        _hideBanner = hide;
        return this;
    }

    public FFmpegCommandBuilder WithNoStats(bool noStats = true)
    {
        _nostats = noStats;
        return this;
    }

    public FFmpegCommandBuilder WithProgressUrl(string progressUrl)
    {
        // Example: "pipe:1" or "http://127.0.0.1:8080/progress"
        AddArgument("-progress", progressUrl);
        return this;
    }

    // ───────────────────────────────────────────────
    // Seeking / trimming
    // ───────────────────────────────────────────────

    public FFmpegCommandBuilder SeekInput(TimeSpan? start)
    {
        if (start.HasValue && start.Value >= TimeSpan.Zero)
            _customPreInputs.AddRange(new[] { "-ss", start.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) });
        return this;
    }

    public FFmpegCommandBuilder SeekOutput(TimeSpan? start)
    {
        if (start.HasValue && start.Value >= TimeSpan.Zero)
            AddArgument("-ss", start.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture));
        return this;
    }

    public FFmpegCommandBuilder Duration(TimeSpan? duration)
    {
        if (duration.HasValue && duration.Value > TimeSpan.Zero)
            AddArgument("-t", duration.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture));
        return this;
    }

    public FFmpegCommandBuilder ToTime(TimeSpan? endTime)
    {
        if (endTime.HasValue && endTime.Value > TimeSpan.Zero)
            AddArgument("-to", endTime.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture));
        return this;
    }

    // ───────────────────────────────────────────────
    // Codecs & quality
    // ───────────────────────────────────────────────

    public FFmpegCommandBuilder VideoCodec(string codec)
    {
        AddArgument("-c:v", codec);
        return this;
    }

    public FFmpegCommandBuilder AudioCodec(string codec)
    {
        AddArgument("-c:a", codec);
        return this;
    }

    public FFmpegCommandBuilder CopyAllStreams()
    {
        AddArgument("-c", "copy");
        return this;
    }

    public FFmpegCommandBuilder CopyVideo()
    {
        AddArgument("-c:v", "copy");
        return this;
    }

    public FFmpegCommandBuilder CopyAudio()
    {
        AddArgument("-c:a", "copy");
        return this;
    }

    public FFmpegCommandBuilder Crf(int crf)
    {
        if (crf < 0 || crf > 51) throw new ArgumentOutOfRangeException(nameof(crf), "CRF must be 0–51");
        AddArgument("-crf", crf.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public FFmpegCommandBuilder Preset(string preset)
    {
        // ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow, placebo
        AddArgument("-preset", preset);
        return this;
    }

    // ───────────────────────────────────────────────
    // Video filters (complex filtergraph support)
    // ───────────────────────────────────────────────

    public FFmpegCommandBuilder AddVideoFilter(string filter)
    {
        if (!string.IsNullOrWhiteSpace(filter))
            _filters.Add(filter.Trim());
        return this;
    }

    public FFmpegCommandBuilder Scale(int width, int? height = null, string flags = "lanczos")
    {
        string h = height.HasValue ? height.Value.ToString(CultureInfo.InvariantCulture) : "-2"; // keep aspect
        _filters.Add($"scale={width}:{h}:flags={flags}");
        return this;
    }

    public FFmpegCommandBuilder Fps(double fps)
    {
        _filters.Add($"fps={fps.ToString("F3", CultureInfo.InvariantCulture)}");
        return this;
    }

    // ───────────────────────────────────────────────
    // Metadata / chapters / subtitles
    // ───────────────────────────────────────────────

    public FFmpegCommandBuilder Metadata(string key, string value)
    {
        AddArgument("-metadata", $"{EscapeMetadataKey(key)}={EscapeArgument(value)}");
        return this;
    }

    public FFmpegCommandBuilder BurnSubtitles(string subtitleFile, string? fontsDir = null)
    {
        var filter = $"subtitles={EscapeFilterArgument(subtitleFile)}";
        if (!string.IsNullOrEmpty(fontsDir))
            filter += $":fontsdir={EscapeFilterArgument(fontsDir)}";

        _filters.Add(filter);
        return this;
    }

    // ───────────────────────────────────────────────
    // Custom / low-level additions
    // ───────────────────────────────────────────────

    public FFmpegCommandBuilder AddArgument(string arg)
    {
        _arguments.Add(arg);
        return this;
    }

    public FFmpegCommandBuilder AddArgument(string key, string value)
    {
        _arguments.Add(key);
        _arguments.Add(EscapeArgument(value));
        return this;
    }

    public FFmpegCommandBuilder AddCustomPreInput(string arg)
    {
        _customPreInputs.Add(arg);
        return this;
    }

    public FFmpegCommandBuilder AddCustomPostInput(string arg)
    {
        _customPostInputs.Add(arg);
        return this;
    }

    // ───────────────────────────────────────────────
    // Build final argument array
    // ───────────────────────────────────────────────

    public string[] Build()
    {
        var final = new List<string>();

        // Global flags from options + builder overrides
        if (_hideBanner) final.Add("-hide_banner");
        if (_nostats) final.Add("-nostats");
        if (_overwrite) final.Add("-y");

        final.AddRange(_customPreInputs);

        if (_inputFile != null)
        {
            final.Add("-i");
            final.Add(EscapeArgument(_inputFile));
        }

        final.AddRange(_customPostInputs);

        // Filters
        if (_filters.Count > 0)
        {
            final.Add("-vf");
            final.Add(string.Join(",", _filters));
        }

        // All other collected arguments
        final.AddRange(_arguments);

        if (_outputFile != null)
        {
            final.Add(EscapeArgument(_outputFile));
        }

        return final.ToArray();
    }

    public string BuildAsString() => string.Join(" ", Build().Select(EscapeForDisplay));

    // ───────────────────────────────────────────────
    // Escaping helpers
    // ───────────────────────────────────────────────

    private static string EscapeArgument(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // Simple case - no special chars
        if (!value.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\'' || c == '\\'))
            return value;

        // Windows / cross-platform safe quoting
        var sb = new StringBuilder();
        sb.Append('"');

        foreach (char c in value)
        {
            if (c == '"')
            {
                sb.Append('\\');
                sb.Append('"');
            }
            else if (c == '\\')
            {
                sb.Append('\\');
                sb.Append('\\');
            }
            else
            {
                sb.Append(c);
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static string EscapeFilterArgument(string value)
    {
        // For filter params like subtitles=filename : escape : , ' etc.
        return value
            .Replace(@"\", @"\\")
            .Replace("'", @"\'")
            .Replace(":", @"\:")
            .Replace("%", @"\%")
            .Replace("#", @"\#");
    }

    private static string EscapeMetadataKey(string key)
    {
        // Very basic – real metadata keys usually don't need much escaping
        return key.Replace("\"", "\\\"");
    }

    private static string EscapeForDisplay(string arg)
    {
        // For logging – show quotes only when really needed
        if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('\''))
            return $"\"{arg}\"";
        return arg;
    }
}