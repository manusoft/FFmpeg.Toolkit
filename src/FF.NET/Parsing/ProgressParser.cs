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

    /// <summary>
    /// Creates a new progress parser.
    /// </summary>
    /// <param name="totalDuration">Optional: known total input duration (from ffprobe) to enable % and ETA calculation.</param>
    /// <param name="progress">Where to push parsed progress updates.</param>
    public ProgressParser(TimeSpan? totalDuration = null, IProgress<FFmpegProgress>? progress = null)
    {
        _totalDuration = totalDuration;
        _progressSink = progress;
    }

    /// <summary>
    /// Feed a chunk of data received from stderr or progress pipe.
    /// Call this from the line-reading loop.
    /// </summary>
    /// <param name="data">New chunk of text (can be partial line).</param>
    public void Feed(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        lock (_lock)
        {
            _buffer.Append(data);

            string bufferContent = _buffer.ToString();

            // Split on double newlines or explicit blank lines – common chunk separator
            var chunks = bufferContent.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < chunks.Length - 1; i++)
            {
                ParseChunk(chunks[i].Trim());
            }

            // Keep the unfinished part
            _buffer.Clear();
            _buffer.Append(chunks[^1]);
        }
    }

    /// <summary>
    /// Call when stream ends (eof) to flush any remaining data.
    /// </summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_buffer.Length > 0)
            {
                ParseChunk(_buffer.ToString().Trim());
                _buffer.Clear();
            }
        }
    }

    private void ParseChunk(string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk)) return;

        var lines = chunk.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            int eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 0) continue;

            string key = trimmed[..eqIndex].Trim();
            string value = trimmed[(eqIndex + 1)..].Trim();

            dict[key] = value;
        }

        if (dict.Count == 0) return;

        var progress = BuildProgress(dict);
        if (progress != null)
        {
            _progressSink?.Report(progress);
        }
    }

    private FFmpegProgress? BuildProgress(IReadOnlyDictionary<string, string> dict)
    {
        if (!dict.TryGetValue("progress", out var status) || string.IsNullOrWhiteSpace(status))
            return null;

        var builder = new FFmpegProgressBuilder
        {
            Status = status.Trim()
        };

        if (dict.TryGetValue("frame", out var frameStr) && long.TryParse(frameStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long frame))
            builder.Frame = frame;

        if (dict.TryGetValue("fps", out var fpsStr) && double.TryParse(fpsStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double fps))
            builder.Fps = fps;

        if (dict.TryGetValue("bitrate", out var bitrate))
            builder.Bitrate = bitrate.Trim();

        if (dict.TryGetValue("speed", out var speed))
            builder.Speed = speed.Trim();

        // Prefer 'out_time' or 'time' – newer ffmpeg uses out_time
        string? timeStr = null;
        if (dict.TryGetValue("out_time", out timeStr) && !string.IsNullOrWhiteSpace(timeStr)) { }
        else if (dict.TryGetValue("time", out timeStr)) { }

        if (!string.IsNullOrWhiteSpace(timeStr) && TryParseFfmpegTime(timeStr, out var timeSpan))
        {
            builder.Time = timeSpan;

            if (_totalDuration.HasValue && _totalDuration.Value > TimeSpan.Zero)
            {
                double percent = (timeSpan.TotalSeconds / _totalDuration.Value.TotalSeconds) * 100;
                builder.PercentComplete = Math.Clamp(percent, 0, 100);

                if (!string.IsNullOrWhiteSpace(builder.Speed) &&
                    builder.Speed.EndsWith("x", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(builder.Speed[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out double speedX) &&
                    speedX > 0.01)
                {
                    double remainingSec = (_totalDuration.Value.TotalSeconds - timeSpan.TotalSeconds) / speedX;
                    builder.Eta = TimeSpan.FromSeconds(Math.Max(0, remainingSec));
                }
            }
        }

        return builder.Build();
    }

    private static bool TryParseFfmpegTime(string timeStr, out TimeSpan result)
    {
        result = default;

        // Format: HH:MM:SS.mmmmmm or SS.mmmmmm or MM:SS.mmm
        var parts = timeStr.Split(':');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out int h) &&
            int.TryParse(parts[1], out int m) &&
            double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double s))
        {
            result = TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(s);
            return true;
        }

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int min) &&
            double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double sec))
        {
            result = TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec);
            return true;
        }

        if (double.TryParse(timeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double totalSec))
        {
            result = TimeSpan.FromSeconds(totalSec);
            return true;
        }

        return false;
    }

    private struct FFmpegProgressBuilder
    {
        public long? Frame { get; set; }   // ← change init → set
        public double? Fps { get; set; }
        public string? Bitrate { get; set; }
        public string? Speed { get; set; }
        public TimeSpan? Time { get; set; }
        public double? PercentComplete { get; set; }
        public TimeSpan? Eta { get; set; }
        public string? Status { get; set; }

        public FFmpegProgress Build() => new()
        {
            Frame = Frame,
            Fps = Fps,
            Bitrate = Bitrate,
            Speed = Speed,
            Time = Time,
            PercentComplete = PercentComplete,
            Eta = Eta,
            Status = Status
        };
    }
}