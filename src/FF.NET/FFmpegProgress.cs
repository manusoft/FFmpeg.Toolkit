namespace ManuHub.FF.NET;

/// <summary>
/// Real-time progress information from an FFmpeg encoding/transcoding operation.
/// </summary>
public record FFmpegProgress
{
    public long? Frame { get; init; }
    public double? Fps { get; init; }
    public string? Bitrate { get; init; }
    public string? Speed { get; init; }
    public TimeSpan? Time { get; init; }
    public double? PercentComplete { get; init; }
    public TimeSpan? Eta { get; init; }
    public string? Status { get; init; }   // "continue" or "end"

    public bool IsFinished => Status == "end";

    public override string ToString()
    {
        var parts = new List<string>();
        if (PercentComplete.HasValue) parts.Add($"{PercentComplete:F1}%");
        if (Time.HasValue) parts.Add($"time={Time.Value.TotalSeconds:F1}s");
        if (Speed != null) parts.Add($"speed={Speed}");
        if (Fps.HasValue) parts.Add($"fps={Fps:F1}");
        if (Bitrate != null) parts.Add($"bitrate={Bitrate}");

        return parts.Count > 0 ? string.Join(" | ", parts) : "No progress data";
    }
}