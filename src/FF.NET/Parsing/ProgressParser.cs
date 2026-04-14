using ManuHub.FF.NET.Abstractions;
using System.Globalization;
using System.Text;

namespace ManuHub.FF.NET.Parsing;

/// <summary>
/// Parses real-time structured progress output from FFmpeg when using -progress pipe:1 or similar.
/// Expects lines in format: key=value\nkey=value\n...\n\n (blank line or progress=end separates updates)
/// </summary>
public class ProgressParser : IProgressParser
{
    private readonly StringBuilder _buffer = new();
    private readonly TimeSpan? _totalDuration;
    private readonly IProgress<FFmpegProgress>? _progressSink;
    private readonly object _lock = new();

    // Accumulator for the latest known good progress
    private FFmpegProgress _current = new FFmpegProgress { Status = "continue" };

    public ProgressParser(TimeSpan? totalDuration = null, IProgress<FFmpegProgress>? progress = null)
    {
        _totalDuration = totalDuration;
        _progressSink = progress;
    }

    public void Feed(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        lock (_lock)
        {
            _buffer.Append(data);

            string content = _buffer.ToString()
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length - 1; i++)
            {
                ParseMessyLine(lines[i]);
            }

            _buffer.Clear();
            if (lines.Length > 0)
                _buffer.Append(lines[^1]);
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            if (_buffer.Length > 0)
                ParseMessyLine(_buffer.ToString());
        }
    }

    private void ParseMessyLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var tokens = line.Split('=', StringSplitOptions.RemoveEmptyEntries);

        bool hasMeaningfulUpdate = false;

        for (int i = 0; i < tokens.Length - 1; i += 2)
        {
            string key = tokens[i].Trim();
            string value = tokens[i + 1].Trim();

            if (string.IsNullOrEmpty(key)) continue;

            hasMeaningfulUpdate |= UpdateField(key, value);
        }

        // Only report when we have a good update (especially time or speed)
        if (hasMeaningfulUpdate && _progressSink != null)
        {
            _progressSink.Report(_current);
        }
    }

    private bool UpdateField(string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "frame":
                if (long.TryParse(value, out long frame))
                    _current = _current with { Frame = frame };
                break;

            case "fps":
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double fps))
                    _current = _current with { Fps = fps };
                break;

            case "bitrate":
                _current = _current with { Bitrate = value };
                break;

            case "speed":
                if (!string.IsNullOrWhiteSpace(value) && value != "N/A")
                    _current = _current with { Speed = value };
                break;

            case "out_time":
            case "time":
                if (TryParseFfmpegTime(value, out var timeSpan))
                {
                    _current = _current with { Time = timeSpan };

                    if (_totalDuration.HasValue && _totalDuration.Value > TimeSpan.Zero)
                    {
                        double percent = (timeSpan.TotalSeconds / _totalDuration.Value.TotalSeconds) * 100;
                        _current = _current with { PercentComplete = Math.Clamp(percent, 0, 100) };
                    }
                }
                break;

            case "progress":
                _current = _current with { Status = value.Trim() };

                // 🔥 ONLY REPORT HERE
                if (_progressSink != null)
                    _progressSink.Report(_current);

                return true;
        }

        return false;
    }

    //private bool UpdateField(string key, string value)
    //{
    //    switch (key.ToLowerInvariant())
    //    {
    //        case "frame":
    //            if (long.TryParse(value, out long frame))
    //            {
    //                _current = _current with { Frame = frame };
    //                return true;
    //            }
    //            break;

    //        case "fps":
    //            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double fps))
    //            {
    //                _current = _current with { Fps = fps };
    //                return true;
    //            }
    //            break;

    //        case "bitrate":
    //            _current = _current with { Bitrate = value };
    //            return true;

    //        case "speed":
    //            if (!string.IsNullOrWhiteSpace(value))
    //            {
    //                _current = _current with { Speed = value };
    //                return true;
    //            }
    //            break;

    //        case "out_time":
    //        case "time":
    //            if (TryParseFfmpegTime(value, out var timeSpan))
    //            {
    //                _current = _current with { Time = timeSpan };

    //                if (_totalDuration.HasValue && _totalDuration.Value > TimeSpan.Zero)
    //                {
    //                    double percent = (timeSpan.TotalSeconds / _totalDuration.Value.TotalSeconds) * 100;
    //                    _current = _current with { PercentComplete = Math.Clamp(percent, 0, 100) };
    //                }
    //                return true;
    //            }
    //            break;

    //        case "progress":
    //            _current = _current with { Status = value.Trim() };
    //            return true;
    //    }

    //    return false;
    //}

    private static bool TryParseFfmpegTime(string timeStr, out TimeSpan result)
    {
        result = default;
        if (TimeSpan.TryParse(timeStr, CultureInfo.InvariantCulture, out result))
            return true;

        if (double.TryParse(timeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double seconds))
        {
            result = TimeSpan.FromSeconds(seconds);
            return true;
        }
        return false;
    }
}