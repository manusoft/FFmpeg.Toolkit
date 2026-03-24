using ManuHub.FF.NET.Models;
using System.Globalization;
using System.Text.Json;

namespace ManuHub.FF.NET.Parsing;

/// <summary>
/// Parses ffprobe JSON output into MediaInfo model.
/// </summary>
public static class MediaInfoParser
{
    public static MediaInfo Parse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var info = new MediaInfo();

        // Format
        if (root.TryGetProperty("format", out var formatElem))
        {
            var fmt = formatElem;
            info.Format = new FormatInfo
            {
                FormatName = fmt.GetPropertyOrDefault("format_name", ""),
                FormatLongName = fmt.GetPropertyOrDefault("format_long_name", ""),
                SizeInBytes = fmt.GetPropertyOrDefault("size", 0L),
                BitRate = fmt.GetPropertyOrDefault("bit_rate", 0L),
                Duration = TimeSpan.FromSeconds(fmt.GetPropertyOrDefault("duration", 0.0))
            };
        }

        // Streams
        if (root.TryGetProperty("streams", out var streamsElem) && streamsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var streamElem in streamsElem.EnumerateArray())
            {
                var stream = ParseStream(streamElem);
                if (stream != null)
                    info.Streams.Add(stream);
            }
        }

        // Chapters
        if (root.TryGetProperty("chapters", out var chaptersElem) && chaptersElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var ch in chaptersElem.EnumerateArray())
            {
                // Chapter title
                string chapterTitle = "";
                if (ch.TryGetProperty("tags", out var tagsElem) &&
                    tagsElem.TryGetProperty("title", out var titleElem))
                {
                    chapterTitle = titleElem.GetString() ?? "";
                }

                info.Chapters.Add(new ChapterInfo
                {
                    Id = ch.GetPropertyOrDefault("id", 0),
                    StartTime = TimeSpan.FromSeconds(ch.GetPropertyOrDefault("start_time", 0.0)),
                    EndTime = TimeSpan.FromSeconds(ch.GetPropertyOrDefault("end_time", 0.0)),
                    Title = chapterTitle,
                });
            }
        }

        return info;
    }

    private static StreamInfo? ParseStream(JsonElement elem)
    {
        var codecType = elem.GetPropertyOrDefault("codec_type", "");

        StreamInfo stream = codecType switch
        {
            "video" => new VideoStreamInfo
            {
                Width = elem.GetPropertyOrDefault("width", 0),
                Height = elem.GetPropertyOrDefault("height", 0),
                PixelFormat = elem.GetPropertyOrDefault("pix_fmt", ""),
                FrameRate = ParseFrameRate(elem.GetPropertyOrDefault("avg_frame_rate", elem.GetPropertyOrDefault("r_frame_rate", "0/1"))),
                BitRate = elem.GetPropertyOrDefault("bit_rate", 0)
            },
            "audio" => new AudioStreamInfo
            {
                Channels = elem.GetPropertyOrDefault("channels", 0),
                SampleRate = elem.GetPropertyOrDefault("sample_rate", 0),
                ChannelLayout = elem.GetPropertyOrDefault("channel_layout", ""),
                BitRate = elem.GetPropertyOrDefault("bit_rate", 0)
            },
            "subtitle" => new SubtitleStreamInfo(),
            _ => null
        };

        if (stream == null) return null;

        // IsDefault (disposition/default)
        int isDefault = 0;
        if (elem.TryGetProperty("disposition", out var dispElem) &&
            dispElem.TryGetProperty("default", out var defElem))
        {
            isDefault = defElem.GetInt32();
        }
        stream.IsDefault = isDefault == 1;

        // Language
        string language = "";
        if (elem.TryGetProperty("tags", out var tagsElem2) &&
            tagsElem2.TryGetProperty("language", out var langElem))
        {
            language = langElem.GetString() ?? "";
        }
        stream.Language = language;

        stream.Index = elem.GetPropertyOrDefault("index", 0);
        stream.CodecName = elem.GetPropertyOrDefault("codec_name", "");
        stream.CodecLongName = elem.GetPropertyOrDefault("codec_long_name", "");
        stream.CodecType = codecType;
        stream.Duration = TimeSpan.FromSeconds(elem.GetPropertyOrDefault("duration", 0.0));
        return stream;
    }

    private static double ParseFrameRate(string rateStr)
    {
        if (string.IsNullOrEmpty(rateStr) || !rateStr.Contains('/')) return 0;
        var parts = rateStr.Split('/');
        if (parts.Length != 2) return 0;
        if (!double.TryParse(parts[0], out var num) || !double.TryParse(parts[1], out var den) || den == 0)
            return 0;
        return num / den;
    }

    private static T GetPropertyOrDefault<T>(this JsonElement elem, string property, T defaultValue)
    {
        if (!elem.TryGetProperty(property, out var prop))
            return defaultValue;

        try
        {
            if (typeof(T) == typeof(string))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return (T)(object)(prop.GetString() ?? "");
                return (T)(object)prop.ToString();
            }

            if (typeof(T) == typeof(int))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return (T)(object)prop.GetInt32();

                if (prop.ValueKind == JsonValueKind.String &&
                    int.TryParse(prop.GetString(), out var v))
                    return (T)(object)v;
            }

            if (typeof(T) == typeof(long))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return (T)(object)prop.GetInt64();

                if (prop.ValueKind == JsonValueKind.String &&
                    long.TryParse(prop.GetString(), out var v))
                    return (T)(object)v;
            }

            if (typeof(T) == typeof(double))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return (T)(object)prop.GetDouble();

                if (prop.ValueKind == JsonValueKind.String &&
                    double.TryParse(prop.GetString(), CultureInfo.InvariantCulture, out var v))
                    return (T)(object)v;
            }
        }
        catch
        {
            // ignore
        }

        return defaultValue;
    }
}