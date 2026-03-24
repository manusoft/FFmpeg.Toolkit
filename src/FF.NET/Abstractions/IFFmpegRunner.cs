using ManuHub.FF.NET.Core;

namespace ManuHub.FF.NET.Abstractions;

/// <summary>
/// Low-level interface responsible for actually launching ffmpeg / ffprobe processes.
/// Makes the library testable (you can mock this interface).
/// </summary>
public interface IFFmpegRunner
{
    /// <summary>
    /// Execute ffmpeg with given arguments and optional progress reporting.
    /// </summary>
    /// <param name="arguments">Command-line arguments (without ffmpeg binary name)</param>
    /// <param name="options">Runtime options (timeout, logger, etc.)</param>
    /// <param name="progress">Optional real-time progress receiver</param>
    /// <param name="knownDuration">Optional known duration</param>
    /// <param name="cancellationToken">Cancellation support</param>
    /// <returns>Process execution result</returns>
    Task<ProcessResult> RunFFmpegAsync(string[] arguments,
                                       FFmpegOptions options,
                                       IProgress<FFmpegProgress>? progress = null,
                                       TimeSpan? knownDuration = null,
                                       CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute ffprobe and capture structured output
    /// </summary>
    Task<ProcessResult> RunFFprobeAsync(string[] arguments,
                                        FFmpegOptions options,
                                        TimeSpan? knownDuration = null,
                                        CancellationToken cancellationToken = default);
}