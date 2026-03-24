using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Core;

namespace ManuHub.FF.NET.Parsing;

/// <summary>
/// Executes ffprobe commands and returns raw output or parsed results.
/// </summary>
public class FFprobeRunner
{
    private readonly IFFmpegRunner _ffmpegRunner;
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
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Most common use case: Get detailed JSON information about a media file.
    /// </summary>
    public async Task<string> GetJsonOutputAsync(
        string inputFile,
        string[]? extraArgs = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputFile))
            throw new ArgumentException("Input file path is required", nameof(inputFile));

        var args = new List<string>
        {
            "-v", "quiet",
            "-print_format", "json",
            "-show_format",
            "-show_streams",
            "-show_chapters",
            "-show_programs",
            "-show_private_data"
        };

        if (extraArgs != null)
            args.AddRange(extraArgs);

        args.Add(EscapePath(inputFile));

        var result = await RunAsync(args.ToArray(), ct).ConfigureAwait(false);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"ffprobe failed (exit code {result.ExitCode}): {result.StandardError.Trim()}");
        }

        return result.StandardOutput.Trim();
    }

    private static string EscapePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        // Quote if contains spaces or special characters
        if (path.Contains(' ') || path.Contains('\'') || path.Contains('"') || path.Contains('\\'))
        {
            return $"\"{path.Replace("\"", "\\\"")}\"";
        }

        return path;
    }
}