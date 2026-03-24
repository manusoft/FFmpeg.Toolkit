using Microsoft.Extensions.Logging;

namespace ManuHub.FF.NET;

/// <summary>
/// Global configuration for the FF.NET library.
/// Most values have sensible defaults; can be overridden per-operation or globally.
/// </summary>
public class FFmpegOptions
{
    public string? FFmpegPath { get; set; }
    public string? FFprobePath { get; set; }

    public string TempDirectory { get; set; } = Path.GetTempPath();
    public bool CleanTemporaryFiles { get; set; } = true;

    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromHours(4);
    public int OutputBufferSize { get; set; } = 8192;

    public ILogger? Logger { get; set; }

    public bool CreateNoWindow { get; set; } = true;
    public bool AlwaysRedirectStandardError { get; set; } = true;

    public string[] GlobalArguments { get; set; } = ["-hide_banner", "-nostats"];

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
