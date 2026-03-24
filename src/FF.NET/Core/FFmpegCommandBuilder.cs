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
    private readonly List<string> _customPreInputs = new();   // before -i (e.g. -ss)
    private readonly List<string> _customPostInputs = new();  // after -i
    private readonly List<string> _filters = new();

    private bool _overwrite = true;
    private bool _hideBanner = true;
    private bool _nostats = true;

    public FFmpegCommandBuilder(FFmpegOptions? options = null)
    {
        _options = options ?? new FFmpegOptions();
    }

    // ───────────────────────────────────────────────
    // Input / Output
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
    // Global Flags
    // ───────────────────────────────────────────────
    public FFmpegCommandBuilder WithHideBanner(bool hide = true) { _hideBanner = hide; return this; }
    public FFmpegCommandBuilder WithNoStats(bool noStats = true) { _nostats = noStats; return this; }

    public FFmpegCommandBuilder WithProgressUrl(string progressUrl)
    {
        AddArgument("-progress", progressUrl);
        return this;
    }

    // ───────────────────────────────────────────────
    // Seeking / Trimming
    // ───────────────────────────────────────────────
    public FFmpegCommandBuilder SeekInput(TimeSpan? start)
    {
        if (start.HasValue && start.Value >= TimeSpan.Zero)
            _customPreInputs.AddRange(new[] { "-ss", start.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) });
        return this;
    }

    public FFmpegCommandBuilder SeekOutput(TimeSpan? start)   // ← Added back for ClipBuilder
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
    // Codecs & Quality
    // ───────────────────────────────────────────────
    public FFmpegCommandBuilder VideoCodec(string codec) { AddArgument("-c:v", codec); return this; }
    public FFmpegCommandBuilder AudioCodec(string codec) { AddArgument("-c:a", codec); return this; }
    public FFmpegCommandBuilder CopyAllStreams() { AddArgument("-c", "copy"); return this; }
    public FFmpegCommandBuilder CopyVideo() { AddArgument("-c:v", "copy"); return this; }
    public FFmpegCommandBuilder CopyAudio() { AddArgument("-c:a", "copy"); return this; }

    public FFmpegCommandBuilder Crf(int crf)
    {
        if (crf < 0 || crf > 51) throw new ArgumentOutOfRangeException(nameof(crf), "CRF must be between 0 and 51");
        AddArgument("-crf", crf.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public FFmpegCommandBuilder Preset(string preset)
    {
        AddArgument("-preset", preset);
        return this;
    }

    // ───────────────────────────────────────────────
    // Filters
    // ───────────────────────────────────────────────
    public FFmpegCommandBuilder AddVideoFilter(string filter)
    {
        if (!string.IsNullOrWhiteSpace(filter))
            _filters.Add(filter.Trim());
        return this;
    }

    public FFmpegCommandBuilder Scale(int width, int? height = null, string flags = "lanczos")
    {
        string h = height?.ToString(CultureInfo.InvariantCulture) ?? "-2";
        _filters.Add($"scale={width}:{h}:flags={flags}");
        return this;
    }

    public FFmpegCommandBuilder Fps(double fps)
    {
        _filters.Add($"fps={fps:F3}");
        return this;
    }

    // ───────────────────────────────────────────────
    // Metadata & Subtitles
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
    // Custom Arguments
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
    // Build
    // ───────────────────────────────────────────────
    public string[] Build()
    {
        var final = new List<string>();

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

        if (_filters.Count > 0)
        {
            final.Add("-vf");
            final.Add(string.Join(",", _filters));
        }

        final.AddRange(_arguments);

        if (_outputFile != null)
            final.Add(EscapeArgument(_outputFile));

        return final.ToArray();
    }

    public string BuildAsString() => string.Join(" ", Build().Select(EscapeForDisplay));

    // ───────────────────────────────────────────────
    // Escaping Helpers
    // ───────────────────────────────────────────────
    private static string EscapeArgument(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!value.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\'' || c == '\\'))
            return value;

        var sb = new StringBuilder();
        sb.Append('"');
        foreach (char c in value)
        {
            if (c == '"') { sb.Append('\\'); sb.Append('"'); }
            else if (c == '\\') { sb.Append('\\'); sb.Append('\\'); }
            else sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string EscapeFilterArgument(string value)
    {
        return value
            .Replace(@"\", @"\\")
            .Replace("'", @"\'")
            .Replace(":", @"\:")
            .Replace("%", @"\%")
            .Replace("#", @"\#");
    }

    private static string EscapeMetadataKey(string key)
    {
        return key.Replace("\"", "\\\"");
    }

    private static string EscapeForDisplay(string arg)
    {
        if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('\''))
            return $"\"{arg}\"";
        return arg;
    }
}