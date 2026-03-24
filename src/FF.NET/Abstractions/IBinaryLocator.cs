namespace ManuHub.FF.NET.Abstractions;

/// <summary>
/// Responsible for locating ffmpeg / ffprobe executables when no explicit path is provided.
/// </summary>
public interface IBinaryLocator
{
    /// <summary>
    /// Attempts to find the ffmpeg executable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Full path to ffmpeg executable or null if not found</returns>
    Task<string?> LocateFFmpegAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to find the ffprobe executable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Full path to ffprobe executable or null if not found</returns>
    Task<string?> LocateFFprobeAsync(CancellationToken cancellationToken = default);
}