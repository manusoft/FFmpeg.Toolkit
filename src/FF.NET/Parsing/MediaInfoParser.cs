using ManuHub.FF.NET.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace ManuHub.FF.NET.Parsing;

/// <summary>
/// Parses ffprobe JSON output into MediaInfo model.
/// </summary>
public static class MediaInfoParser
{
    //private static readonly JsonElement EmptyJsonObject = JsonDocument.Parse("{}").RootElement;

    public static MediaInfo Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new MediaInfo();

        //var doc = JsonDocument.Parse(json);
        //var root = doc.RootElement;

        var info = new MediaInfo();

        var mediaRoot = JsonSerializer.Deserialize<MediaInfo>(json);
        if (mediaRoot == null) return new MediaInfo();

        // Format
        //if (mediaRoot.Format != null) //(root.TryGetProperty("format", out var formatElem))
        //{
        //    info.Format = new FormatInfo
        //    {
        //        FormatName = mediaRoot.Format.FormatName, //formatElem.GetPropertyOrDefault("format_name", ""),
        //        FormatLongName = mediaRoot.Format.FormatLongName, //formatElem.GetPropertyOrDefault("format_long_name", ""),
        //        SizeInBytes = long.Parse(mediaRoot.Format.Size), //formatElem.GetPropertyOrDefault("size", 0L),
        //        BitRate = long.Parse(mediaRoot.Format.BitRate), //formatElem.GetPropertyOrDefault("bit_rate", 0L),
        //        Duration = double.TryParse(mediaRoot.Format.Duration, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds)
        //           ? TimeSpan.FromSeconds(seconds)
        //           : TimeSpan.Zero, //TimeSpan.FromSeconds(formatElem.GetPropertyOrDefault("duration", 0.0))
        //    };
        //}

        // Streams
        //if (mediaRoot.Streams != null) //(root.TryGetProperty("streams", out var streamsElem) && streamsElem.ValueKind == JsonValueKind.Array)
        //{
        //    foreach (var stream in mediaRoot.Streams)  //(var streamElem in streamsElem.EnumerateArray())
        //    {
        //        //var stream = ParseStream(streamElem);
        //        if (stream != null)
        //        {
        //            var streamInfo = new StreamInfo
        //            {
        //                CodecName = stream.CodecName,
        //                CodecLongName = stream.CodecLongName,
        //            };
        //            //info.Streams.Add(stream);
        //        }
        //    }
        //}

        // Chapters
        //if (root.TryGetProperty("chapters", out var chaptersElem) && chaptersElem.ValueKind == JsonValueKind.Array)
        //{
        //    foreach (var ch in chaptersElem.EnumerateArray())
        //    {
        //        string title = "";
        //        if (ch.TryGetProperty("tags", out var tags) && tags.TryGetProperty("title", out var titleElem))
        //            title = titleElem.GetString() ?? "";

        //        info.Chapters.Add(new ChapterInfo
        //        {
        //            Id = ch.GetPropertyOrDefault("id", 0),
        //            StartTime = TimeSpan.FromSeconds(ch.GetPropertyOrDefault("start_time", 0.0)),
        //            EndTime = TimeSpan.FromSeconds(ch.GetPropertyOrDefault("end_time", 0.0)),
        //            Title = title
        //        });
        //    }
        //}

        return info;
    }

    //private static StreamInfo? ParseStream(JsonElement elem)
    //{
    //    var codecType = elem.GetPropertyOrDefault("codec_type", "");

    //    StreamInfo? stream = codecType switch
    //    {
    //        "video" => new VideoStreamInfo
    //        {
    //            Width = elem.GetPropertyOrDefault("width", 0),
    //            Height = elem.GetPropertyOrDefault("height", 0),
    //            PixelFormat = elem.GetPropertyOrDefault("pix_fmt", ""),
    //            FrameRate = ParseFrameRate(elem.GetPropertyOrDefault("avg_frame_rate", "0/1")),
    //            BitRate = elem.GetPropertyOrDefault("bit_rate", 0)
    //        },
    //        "audio" => new AudioStreamInfo
    //        {
    //            Channels = elem.GetPropertyOrDefault("channels", 0),
    //            SampleRate = elem.GetPropertyOrDefault("sample_rate", 0),
    //            ChannelLayout = elem.GetPropertyOrDefault("channel_layout", ""),
    //            BitRate = elem.GetPropertyOrDefault("bit_rate", 0)
    //        },
    //        "subtitle" => new SubtitleStreamInfo(),
    //        _ => null
    //    };

    //    if (stream == null) return null;

    //    // Common properties
    //    stream.Index = elem.GetPropertyOrDefault("index", 0);
    //    stream.CodecName = elem.GetPropertyOrDefault("codec_name", "");
    //    stream.CodecLongName = elem.GetPropertyOrDefault("codec_long_name", "");
    //    stream.CodecType = codecType;
    //    stream.Duration = TimeSpan.FromSeconds(elem.GetPropertyOrDefault("duration", 0.0));

    //    // IsDefault
    //    stream.IsDefault = elem.GetPropertyOrDefault("disposition", EmptyJsonObject)
    //                          .GetPropertyOrDefault("default", 0) == 1;

    //    // Language
    //    stream.Language = elem.GetPropertyOrDefault("tags", EmptyJsonObject)
    //                         .GetPropertyOrDefault("language", "");

    //    return stream;
    //}

    //private static double ParseFrameRate(string rateStr)
    //{
    //    if (string.IsNullOrEmpty(rateStr) || !rateStr.Contains('/')) return 0;

    //    var parts = rateStr.Split('/');
    //    if (parts.Length != 2) return 0;

    //    if (!double.TryParse(parts[0], out var num) ||
    //        !double.TryParse(parts[1], out var den) || den == 0)
    //        return 0;

    //    return num / den;
    //}

    // Improved extension method
    //private static T GetPropertyOrDefault<T>(this JsonElement elem, string property, T defaultValue)
    //{
    //    if (!elem.TryGetProperty(property, out var prop))
    //        return defaultValue;

    //    try
    //    {
    //        if (typeof(T) == typeof(string))
    //            return (T)(object)(prop.GetString() ?? "");

    //        if (typeof(T) == typeof(int) && prop.ValueKind == JsonValueKind.Number)
    //            return (T)(object)prop.GetInt32();

    //        if (typeof(T) == typeof(long) && prop.ValueKind == JsonValueKind.Number)
    //            return (T)(object)prop.GetInt64();

    //        if (typeof(T) == typeof(double) && prop.ValueKind == JsonValueKind.Number)
    //            return (T)(object)prop.GetDouble();
    //    }
    //    catch
    //    {
    //        // ignore conversion errors
    //    }

    //    return defaultValue;
    //}
}