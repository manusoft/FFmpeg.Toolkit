namespace ManuHub.FF.NET.Models;

/// <summary>
/// Parsed media information from ffprobe (streams, format, chapters).
/// </summary>
public class MediaInfo
{
    public FormatInfo Format { get; set; } = new();
    public List<StreamInfo> Streams { get; } = new();
    public List<ChapterInfo> Chapters { get; } = new();

    public TimeSpan Duration => Format.Duration > TimeSpan.Zero
        ? Format.Duration
        : FindLongestVideoStream()?.Duration ?? TimeSpan.Zero;

    public VideoStreamInfo? VideoStream => Streams.OfType<VideoStreamInfo>().FirstOrDefault();
    public AudioStreamInfo? AudioStream => Streams.OfType<AudioStreamInfo>().FirstOrDefault(s => s.IsDefault);

    private VideoStreamInfo? FindLongestVideoStream()
    {
        return Streams.OfType<VideoStreamInfo>()
            .OrderByDescending(s => s.Duration.TotalSeconds)
            .FirstOrDefault();
    }
}

public class FormatInfo
{
    public string FormatName { get; set; } = string.Empty;
    public string FormatLongName { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public TimeSpan Duration { get; set; }
    public long BitRate { get; set; }
}

public abstract class StreamInfo
{
    public int Index { get; set; }
    public string CodecName { get; set; } = string.Empty;
    public string CodecLongName { get; set; } = string.Empty;
    public string CodecType { get; set; } = string.Empty; // video, audio, subtitle
    public TimeSpan Duration { get; set; }
    public bool IsDefault { get; set; }
    public string Language { get; set; } = string.Empty;
}

public class VideoStreamInfo : StreamInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
    public string PixelFormat { get; set; } = string.Empty;
    public double FrameRate { get; set; }
    public int BitRate { get; set; }
}

public class AudioStreamInfo : StreamInfo
{
    public int Channels { get; set; }
    public int SampleRate { get; set; }
    public string ChannelLayout { get; set; } = string.Empty;
    public int BitRate { get; set; }
}

public class SubtitleStreamInfo : StreamInfo
{
    // Add more subtitle-specific fields later if needed
}

public class ChapterInfo
{
    public int Id { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string Title { get; set; } = string.Empty;
}