using ManuHub.FF.NET.Core;

namespace ManuHub.FF.NET.Builders;

/// <summary>
/// Fluent builder for constructing FFmpeg filter graphs (video and audio filters).
/// Supports both simple -vf chains and complex filter_complex graphs with labels.
/// </summary>
public class FilterGraphBuilder
{
    private readonly List<string> _videoFilters = new();
    private readonly List<string> _audioFilters = new();
    private readonly List<string> _complexInputs = new();   // e.g. [0:v], [1:v], ...
    private readonly List<string> _complexOutputs = new();  // [vout], [aout], ...
    private bool _useComplex = false;
    private string _lastVideoLabel = "[v]";
    private string _lastAudioLabel = "[a]";

    private readonly FFmpegCommandBuilder _parentCommandBuilder;

    public FilterGraphBuilder(FFmpegCommandBuilder parentCommandBuilder)
    {
        _parentCommandBuilder = parentCommandBuilder ?? throw new ArgumentNullException(nameof(parentCommandBuilder));
    }

    // ───────────────────────────────────────────────
    // Chain style – simple filters (added to -vf / -af)
    // ───────────────────────────────────────────────

    public FilterGraphBuilder Scale(int width, int? height = null, string flags = "lanczos")
    {
        string h = height.HasValue ? height.Value.ToString() : "-2";
        AddVideoFilter($"scale={width}:{h}:flags={flags}");
        return this;
    }

    public FilterGraphBuilder Fps(double fps, string round = "near")
    {
        AddVideoFilter($"fps={fps:F3}:round={round}");
        return this;
    }

    public FilterGraphBuilder Crop(int width, int height, int? x = null, int? y = null)
    {
        string pos = (x.HasValue && y.HasValue) ? $"{x.Value}:{y.Value}" : "in_w/2-(iw/2):in_h/2-(ih/2)";
        AddVideoFilter($"crop={width}:{height}:{pos}");
        return this;
    }

    public FilterGraphBuilder Pad(int width, int height, int? x = null, int? y = null, string color = "black")
    {
        string pos = (x.HasValue && y.HasValue) ? $"{x.Value}:{y.Value}" : "(ow-iw)/2:(oh-ih)/2";
        AddVideoFilter($"pad={width}:{height}:{pos}:{color}");
        return this;
    }

    public FilterGraphBuilder Rotate(double angleDegrees, string fillColor = "black")
    {
        double rad = angleDegrees * Math.PI / 180;
        AddVideoFilter($"rotate={rad}:ow=rotw(iw):oh=roth(ih):c={fillColor}");
        return this;
    }

    public FilterGraphBuilder Denoise(string preset = "nlmeans", double strength = 1.0)
    {
        if (preset == "nlmeans")
            AddVideoFilter($"nlmeans=s={strength}");
        else if (preset == "hqdn3d")
            AddVideoFilter($"hqdn3d");
        return this;
    }

    public FilterGraphBuilder Deinterlace(string mode = "yadif")
    {
        AddVideoFilter(mode);
        return this;
    }

    public FilterGraphBuilder FadeIn(double durationSeconds, string type = "linear")
    {
        AddVideoFilter($"fade=t=in:st=0:d={durationSeconds}:type={type}");
        return this;
    }

    public FilterGraphBuilder FadeOut(double durationSeconds, string type = "linear")
    {
        AddVideoFilter($"fade=t=out:st={durationSeconds}:d={durationSeconds}:type={type}");
        return this;
    }

    public FilterGraphBuilder SetDar(string dar = "16/9")
    {
        AddVideoFilter($"setdar={dar}");
        return this;
    }

    public FilterGraphBuilder SetSar(string sar = "1/1")
    {
        AddVideoFilter($"setsar={sar}");
        return this;
    }

    // ───────────────────────────────────────────────
    // Audio filters
    // ───────────────────────────────────────────────

    public FilterGraphBuilder Volume(double gainDb)
    {
        AddAudioFilter($"volume={gainDb}dB");
        return this;
    }

    public FilterGraphBuilder NormalizeLoudness(double targetLUFS = -23.0)
    {
        AddAudioFilter($"loudnorm=I={targetLUFS}:TP=-1:LRA=11");
        return this;
    }

    public FilterGraphBuilder Atrim(TimeSpan start, TimeSpan? duration = null)
    {
        string dur = duration.HasValue ? $":duration={duration.Value.TotalSeconds:F3}" : "";
        AddAudioFilter($"atrim=start={start.TotalSeconds:F3}{dur}");
        return this;
    }

    // ───────────────────────────────────────────────
    // Complex filter graph support
    // ───────────────────────────────────────────────

    public FilterGraphBuilder UseComplexGraph()
    {
        _useComplex = true;
        return this;
    }

    public FilterGraphBuilder AddComplexInput(string label)
    {
        _complexInputs.Add(label);
        return this;
    }

    public FilterGraphBuilder AddComplexFilter(string filterString)
    {
        _filterParts.Add(filterString);
        return this;
    }

    // ───────────────────────────────────────────────
    // Apply to parent command builder
    // ───────────────────────────────────────────────

    public FFmpegCommandBuilder Apply()
    {
        if (_useComplex || _complexInputs.Count > 0)
        {
            string complexFilter = BuildComplexFilterString();
            if (!string.IsNullOrWhiteSpace(complexFilter))
            {
                _parentCommandBuilder.AddArgument("-filter_complex", complexFilter);

                // Map last output if needed
                if (_complexOutputs.Count > 0)
                {
                    _parentCommandBuilder.AddArgument("-map", _complexOutputs.Last());
                }
            }
        }
        else
        {
            // Simple mode
            if (_videoFilters.Count > 0)
            {
                _parentCommandBuilder.AddArgument("-vf", string.Join(",", _videoFilters));
            }

            if (_audioFilters.Count > 0)
            {
                _parentCommandBuilder.AddArgument("-af", string.Join(",", _audioFilters));
            }
        }

        return _parentCommandBuilder;
    }

    // ───────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────

    private void AddVideoFilter(string filter)
    {
        if (_useComplex)
        {
            string newLabel = $"[v{_videoFilters.Count + 1}]";
            _filterParts.Add($"{_lastVideoLabel}{filter}{newLabel}");
            _lastVideoLabel = newLabel;
        }
        else
        {
            _videoFilters.Add(filter);
        }
    }

    private void AddAudioFilter(string filter)
    {
        if (_useComplex)
        {
            string newLabel = $"[a{_audioFilters.Count + 1}]";
            _filterParts.Add($"{_lastAudioLabel}{filter}{newLabel}");
            _lastAudioLabel = newLabel;
        }
        else
        {
            _audioFilters.Add(filter);
        }
    }

    private string BuildComplexFilterString()
    {
        var parts = new List<string>();

        // inputs already added via AddComplexInput or parent builder
        parts.AddRange(_filterParts);

        // final output label(s)
        if (_lastVideoLabel != "[v]")
            _complexOutputs.Add(_lastVideoLabel);
        if (_lastAudioLabel != "[a]")
            _complexOutputs.Add(_lastAudioLabel);

        return string.Join(";", parts);
    }

    private readonly List<string> _filterParts = new();
}