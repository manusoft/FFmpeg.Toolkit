using ManuHub.FF.NET.Abstractions;
using System.Runtime.InteropServices;

namespace ManuHub.FF.NET.Core;

/// <summary>
/// Default implementation that searches common locations and PATH.
/// </summary>
public class DefaultBinaryLocator : IBinaryLocator
{
    private static readonly string[] PossibleFFmpegNames =
    [
        "ffmpeg",
        "ffmpeg.exe"
    ];

    private static readonly string[] PossibleFFprobeNames =
    [
        "ffprobe",
        "ffprobe.exe"
    ];

    private readonly string[] _searchPaths;

    public DefaultBinaryLocator()
    {
        _searchPaths = GetSearchPaths().ToArray();
    }

    public async Task<string?> LocateFFmpegAsync(CancellationToken ct = default)
    {
        return await LocateAsync(PossibleFFmpegNames, ct).ConfigureAwait(false);
    }

    public async Task<string?> LocateFFprobeAsync(CancellationToken ct = default)
    {
        return await LocateAsync(PossibleFFprobeNames, ct).ConfigureAwait(false);
    }

    private async Task<string?> LocateAsync(string[] names, CancellationToken ct)
    {
        // 1. Check explicit PATH entries
        foreach (var name in names)
        {
            var fullPath = FindInPath(name);
            if (fullPath is not null && File.Exists(fullPath))
                return fullPath;
        }

        // 2. Check common installation folders (async just for consistency)
        foreach (var dir in _searchPaths)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string? FindInPath(string filename)
    {
        if (Path.IsPathRooted(filename) && File.Exists(filename))
            return filename;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in paths)
        {
            try
            {
                var full = Path.Combine(path, filename);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                // ignore invalid paths
            }
        }

        return null;
    }

    private static IEnumerable<string> GetSearchPaths()
    {
        // Very common locations – feel free to extend
        yield return @"C:\ffmpeg\bin";
        yield return @"C:\Program Files\ffmpeg\bin";
        yield return @"C:\Program Files (x86)\ffmpeg\bin";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            yield return "/usr/bin";
            yield return "/usr/local/bin";
            yield return "/opt/ffmpeg/bin";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/usr/local/bin";
            yield return "/opt/homebrew/bin";
        }

        // Current directory as last resort
        yield return Directory.GetCurrentDirectory();
    }
}