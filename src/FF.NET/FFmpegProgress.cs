namespace ManuHub.FF.NET;

/// <summary>
/// Real-time progress information from an FFmpeg encoding/transcoding operation.
/// </summary>
public class FFmpegProgress
{
    /// <summary>
    /// Number of frames processed so far.
    /// </summary>
    public long? Frame { get; init; }

    /// <summary>
    /// Current processing speed in frames per second.
    /// </summary>
    public double? Fps { get; init; }

    /// <summary>
    /// Current output bitrate (e.g. "4500kb/s", "N/A").
    /// </summary>
    public string? Bitrate { get; init; }

    /// <summary>
    /// Processing speed multiplier (e.g. "2.3x", "0.8x").
    /// </summary>
    public string? Speed { get; init; }

    /// <summary>
    /// Parsed current output time / position.
    /// </summary>
    public TimeSpan? Time { get; init; }

    /// <summary>
    /// Estimated percentage complete (0–100), requires known total duration.
    /// Null if total duration is unknown.
    /// </summary>
    public double? PercentComplete { get; init; }

    /// <summary>
    /// ETA (estimated time remaining), requires total duration and speed.
    /// </summary>
    public TimeSpan? Eta { get; init; }

    /// <summary>
    /// Current status: "continue", "end", or other.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Whether encoding appears finished successfully.
    /// </summary>
    public bool IsFinished => Status == "end";

    public override string ToString()
    {
        var parts = new List<string>();
        if (PercentComplete.HasValue) parts.Add($"{PercentComplete:F1}%");
        if (Time.HasValue) parts.Add($"time={Time.Value.TotalSeconds:F1}s");
        if (Speed is not null) parts.Add($"speed={Speed}");
        if (Fps.HasValue) parts.Add($"fps={Fps:F1}");
        if (parts.Count == 0) return "No progress data";
        return string.Join(" | ", parts);
    }
}