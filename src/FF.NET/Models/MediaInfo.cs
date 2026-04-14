using System.Globalization;
using System.Text.Json.Serialization;

namespace ManuHub.FF.NET.Models;


public class MediaInfo
{
    [JsonPropertyName("streams")] public List<Stream>? Streams { get; set; }
    [JsonPropertyName("chapters")] public List<Chapter>? Chapters { get; set; }
    [JsonPropertyName("format")] public Format? Format { get; set; }

    public TimeSpan Duration => double.TryParse(Format?.Duration, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds)
                   ? TimeSpan.FromSeconds(seconds)
                   : FindLongestVideoStream();

    public Stream? VideoStream => Streams?.FirstOrDefault(x => x.CodecType == "video");
    public Stream? AudioStream => Streams?.FirstOrDefault(x => x.CodecType == "audio");
    public Stream? SubtitleStream => Streams?.FirstOrDefault(x => x.CodecType == "subtitle");

    private TimeSpan FindLongestVideoStream()
    {
        var stream = Streams?
            .Select(s => new
            {
                Stream = s,
                Seconds = double.TryParse(s.Duration ?? s.Tags?["DURATION"], NumberStyles.Any, CultureInfo.InvariantCulture, out var sec) ? sec : 0
            })
            .OrderByDescending(x => x.Seconds)
            .FirstOrDefault();

        return stream != null
            ? TimeSpan.FromSeconds(stream.Seconds)
            : TimeSpan.Zero;
    }
}

public class Chapter
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("time_base")] public string TimeBase { get; set; } = string.Empty;

    [JsonPropertyName("start")] public long Start { get; set; }
    [JsonPropertyName("start_time")] public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("end")] public long End { get; set; }
    [JsonPropertyName("end_time")] public string EndTime { get; set; } = string.Empty;

    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public class Format
{
    [JsonPropertyName("filename")] public string? Filename { get; set; }
    [JsonPropertyName("nb_streams")] public int NbStreams { get; set; }
    [JsonPropertyName("nb_programs")] public int NbPrograms { get; set; }
    [JsonPropertyName("nb_stream_groups")] public int NbStreamGroups { get; set; }
    [JsonPropertyName("format_name")] public string FormatName { get; set; } = string.Empty;
    [JsonPropertyName("format_long_name")] public string FormatLongName { get; set; } = string.Empty;
    [JsonPropertyName("start_time")] public string StartTime { get; set; } = string.Empty;
    [JsonPropertyName("duration")] public string Duration { get; set; } = string.Empty;
    [JsonPropertyName("size")] public string Size { get; set; } = string.Empty;
    [JsonPropertyName("bit_rate")] public string BitRate { get; set; } = string.Empty;
    [JsonPropertyName("probe_score")] public int ProbeScore { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public class Stream
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("codec_name")] public string CodecName { get; set; } = string.Empty;
    [JsonPropertyName("codec_long_name")] public string CodecLongName { get; set; } = string.Empty;
    [JsonPropertyName("profile")] public string Profile { get; set; } = string.Empty;
    [JsonPropertyName("codec_type")] public string CodecType { get; set; } = string.Empty;
    [JsonPropertyName("codec_tag_string")] public string CodecTagString { get; set; } = string.Empty;
    [JsonPropertyName("codec_tag")] public string CodecTag { get; set; } = string.Empty;

    [JsonPropertyName("mime_codec_string")] public string MimeCodecString { get; set; } = string.Empty;

    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }

    [JsonPropertyName("coded_width")] public int CodedWidth { get; set; }
    [JsonPropertyName("coded_height")] public int CodedHeight { get; set; }
    [JsonPropertyName("has_b_frames")] public int HasBFrames { get; set; }

    [JsonPropertyName("sample_aspect_ratio")] public string SampleAspectRatio { get; set; } = string.Empty;
    [JsonPropertyName("display_aspect_ratio")] public string DisplayAspectRatio { get; set; } = string.Empty;

    [JsonPropertyName("pix_fmt")] public string PixelFormat { get; set; } = string.Empty;
    [JsonPropertyName("level")] public int Level { get; set; }

    [JsonPropertyName("color_range")] public string ColorRange { get; set; } = string.Empty;
    [JsonPropertyName("color_space")] public string ColorSpace { get; set; } = string.Empty;
    [JsonPropertyName("color_transfer")] public string ColorTransfer { get; set; } = string.Empty;
    [JsonPropertyName("color_primaries")] public string ColorPrimaries { get; set; } = string.Empty;

    [JsonPropertyName("chroma_location")] public string ChromaLocation { get; set; } = string.Empty;
    [JsonPropertyName("field_order")] public string FieldOrder { get; set; } = string.Empty;
    [JsonPropertyName("is_avc")] public string IsAvc { get; set; } = string.Empty;
    [JsonPropertyName("nal_length_size")] public string NalLengthSize { get; set; } = string.Empty;
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;


    [JsonPropertyName("r_frame_rate")] public string RFrameRate { get; set; } = string.Empty;
    [JsonPropertyName("avg_frame_rate")] public string AvgFrameRate { get; set; } = string.Empty;
    [JsonPropertyName("time_base")] public string TimeBase { get; set; } = string.Empty;

    [JsonPropertyName("start_pts")] public int StartPts { get; set; }
    [JsonPropertyName("start_time")] public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("duration_ts")] public int DurationTs { get; set; }

    [JsonPropertyName("duration")] public string Duration { get; set; } = string.Empty;

    [JsonPropertyName("bit_rate")] public string BitRate { get; set; } = string.Empty;
    [JsonPropertyName("bits_per_raw_sample")] public string BitsPerRawSample { get; set; } = string.Empty;
    [JsonPropertyName("nb_frames")] public string NbFrames { get; set; } = string.Empty;
    [JsonPropertyName("extradata_size")] public int ExtradataSize { get; set; }

    [JsonPropertyName("disposition")] public Disposition Disposition { get; set; } = null!;

    [JsonPropertyName("sample_fmt")] public string SampleFmt { get; set; } = string.Empty;
    [JsonPropertyName("sample_rate")] public string SampleRate { get; set; } = string.Empty;
    [JsonPropertyName("channels")] public int channels { get; set; }
    [JsonPropertyName("channel_layout")] public string ChannelLayout { get; set; } = string.Empty;
    [JsonPropertyName("bits_per_sample")] public int BitsPerSample { get; set; }
    [JsonPropertyName("initial_padding")] public int InitialPadding { get; set; }

    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public class Disposition
{
    [JsonPropertyName("_default")] public int Default { get; set; }
    [JsonPropertyName("dub")] public int Dub { get; set; }
    [JsonPropertyName("original")] public int Original { get; set; }
    [JsonPropertyName("comment")] public int Comment { get; set; }
    [JsonPropertyName("lyrics")] public int Lyrics { get; set; }
    [JsonPropertyName("karaoke")] public int Karaoke { get; set; }
    [JsonPropertyName("forced")] public int Forced { get; set; }
    [JsonPropertyName("hearing_impaired")] public int HearingImpaired { get; set; }
    [JsonPropertyName("visual_impaired")] public int VisualImpaired { get; set; }
    [JsonPropertyName("clean_effects")] public int CleanEffects { get; set; }
    [JsonPropertyName("attached_pic")] public int AttachedPic { get; set; }
    [JsonPropertyName("timed_thumbnails")] public int TimedThumbnails { get; set; }
    [JsonPropertyName("non_diegetic")] public int NonDiegetic { get; set; }
    [JsonPropertyName("captions")] public int Captions { get; set; }
    [JsonPropertyName("descriptions")] public int Descriptions { get; set; }
    [JsonPropertyName("metadata")] public int Metadata { get; set; }
    [JsonPropertyName("dependent")] public int Dependent { get; set; }
    [JsonPropertyName("still_image")] public int StillImage { get; set; }
    [JsonPropertyName("multilayer")] public int Multilayer { get; set; }
}

public class FormatTags
{
    [JsonPropertyName("major_brand")] public string MajorBrand { get; set; } = string.Empty;
    [JsonPropertyName("minor_version")] public string MinorVersion { get; set; } = string.Empty;
    [JsonPropertyName("compatible_brands")] public string CompatibleBrands { get; set; } = string.Empty;
}

public class StreamTags
{
    [JsonPropertyName("language")] public string Language { get; set; } = string.Empty;
    [JsonPropertyName("handler_name")] public string HandlerName { get; set; } = string.Empty;
    [JsonPropertyName("encoder")] public string Encoder { get; set; } = string.Empty;
}
