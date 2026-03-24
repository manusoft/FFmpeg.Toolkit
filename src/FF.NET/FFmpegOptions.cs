using Microsoft.Extensions.Logging;

namespace ManuHub.FF.NET;

/// <summary>
/// Global configuration for the FF.NET library.
/// Most values have sensible defaults; can be overridden per-operation or globally.
/// </summary>
public class FFmpegOptions
{
    /// <summary>
    /// Full path to ffmpeg executable (ffmpeg.exe / ffmpeg).
    /// If null, auto-discovery is attempted via <see cref="IBinaryLocator"/>.
    /// </summary>
    public string? FFmpegPath { get; set; }

    /// <summary>
    /// Full path to ffprobe executable (ffprobe.exe / ffprobe).
    /// If null, auto-discovery is attempted.
    /// </summary>
    public string? FFprobePath { get; set; }

    /// <summary>
    /// Directory where temporary files are created (concat lists, filter scripts, etc.).
    /// Defaults to Path.GetTempPath().
    /// </summary>
    public string TempDirectory { get; set; } = Path.GetTempPath();

    /// <summary>
    /// Whether temporary files should be automatically deleted after use.
    /// Default: true
    /// </summary>
    public bool CleanTemporaryFiles { get; set; } = true;

    /// <summary>
    /// Default timeout for FFmpeg operations (after which process is killed).
    /// Use TimeSpan.Zero or negative value for no timeout.
    /// Default: 4 hours
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromHours(4);

    /// <summary>
    /// Buffer size for reading stdout/stderr (in bytes).
    /// Larger values = fewer allocations on very long outputs.
    /// Default: 8192
    /// </summary>
    public int OutputBufferSize { get; set; } = 8192;

    /// <summary>
    /// Logger to use for diagnostic & error messages.
    /// If null, no logging occurs.
    /// </summary>
    public ILogger? Logger { get; set; }

    /// <summary>
    /// Whether to hide the console window on Windows (only relevant when not redirected).
    /// Default: true
    /// </summary>
    public bool CreateNoWindow { get; set; } = true;

    /// <summary>
    /// Whether to redirect standard error even when progress parsing is not used.
    /// Usually left true.
    /// </summary>
    public bool AlwaysRedirectStandardError { get; set; } = true;

    /// <summary>
    /// Additional global arguments added to every ffmpeg invocation
    /// (e.g. "-hide_banner -nostats" is very common).
    /// </summary>
    public string[] GlobalArguments { get; set; } = ["-hide_banner", "-nostats"];

    /// <summary>
    /// Creates a deep copy of current options (useful for per-operation overrides).
    /// </summary>
    public FFmpegOptions Clone()
    {
        return new FFmpegOptions
        {
            FFmpegPath = FFmpegPath,
            FFprobePath = FFprobePath,
            TempDirectory = TempDirectory,
            CleanTemporaryFiles = CleanTemporaryFiles,
            DefaultTimeout = DefaultTimeout,
            OutputBufferSize = OutputBufferSize,
            Logger = Logger,
            CreateNoWindow = CreateNoWindow,
            AlwaysRedirectStandardError = AlwaysRedirectStandardError,
            GlobalArguments = (string[])GlobalArguments.Clone()
        };
    }
}
