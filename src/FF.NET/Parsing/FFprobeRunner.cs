using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Core;

namespace ManuHub.FF.NET.Parsing;

/// <summary>
/// Executes ffprobe commands and returns raw output or parsed results.
/// </summary>
public class FFprobeRunner
{
    private readonly IFFmpegRunner _ffmpegRunner; // Reuse the same runner interface
    private readonly FFmpegOptions _options;

    public FFprobeRunner(IFFmpegRunner ffmpegRunner, FFmpegOptions? options = null)
    {
        _ffmpegRunner = ffmpegRunner ?? throw new ArgumentNullException(nameof(ffmpegRunner));
        _options = options ?? new FFmpegOptions();
    }

    public async Task<ProcessResult> RunAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        return await _ffmpegRunner.RunFFprobeAsync(
            arguments,
            _options,
            null,
            cancellationToken);
    }

    // Convenience method: get JSON output (most common use case)
    public async Task<string> GetJsonOutputAsync(string inputFile,
                                                 string[]? extraArgs = null,
                                                 CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "-v", "quiet",
            "-print_format", "json",
            "-show_format",
            "-show_streams",
            "-show_chapters",
            "-show_programs"
        };

        if (extraArgs != null)
            args.AddRange(extraArgs);

        args.Add(EscapePath(inputFile));

        var result = await RunAsync(args.ToArray(), ct);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"ffprobe failed (exit {result.ExitCode}): {result.StandardError}");
        }

        return result.StandardOutput;
    }

    private static string EscapePath(string path)
    {
        // Basic escaping for ffprobe arguments
        if (path.Contains(" ") || path.Contains("'") || path.Contains("\""))
            return $"\"{path.Replace("\"", "\\\"")}\"";
        return path;
    }
}