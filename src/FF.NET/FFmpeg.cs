using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Builders;
using ManuHub.FF.NET.Core;
using Microsoft.Extensions.Logging;

namespace ManuHub.FF.NET;

/// <summary>
/// Static facade / entry point for the FFmpeg.Toolkit library.
/// Provides factory methods for all major builders with sensible defaults.
/// </summary>
public static class FFmpeg
{
    private static IFFmpegRunner? _defaultRunner;
    private static FFmpegOptions? _globalOptions;

    /// <summary>
    /// Global/default options (can be overridden per call).
    /// Set once at startup if needed.
    /// </summary>
    public static FFmpegOptions GlobalOptions
    {
        get => _globalOptions ??= new FFmpegOptions();
        set => _globalOptions = value;
    }

    /// <summary>
    /// Default runner instance (lazy-created).
    /// Uses DefaultBinaryLocator + optional logger.
    /// </summary>
    public static IFFmpegRunner DefaultRunner
    {
        get
        {
            if (_defaultRunner == null)
            {
                var locator = new DefaultBinaryLocator();
                var logger = GetLogger<FFmpegProcessRunner>(); // optional – from DI or fallback
                _defaultRunner = new FFmpegProcessRunner(locator, logger);
            }
            return _defaultRunner;
        }
    }

    // ───────────────────────────────────────────────
    // Builder Factories
    // ───────────────────────────────────────────────

    public static ConvertBuilder Convert(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
    {
        return new ConvertBuilder(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());
    }

    public static GenerateThumbnailsBuilder Thumbnails(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
    {
        return new GenerateThumbnailsBuilder(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());
    }

    public static ExtractAudioBuilder ExtractAudio(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
    {
        return new ExtractAudioBuilder(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());
    }

    public static ClipBuilder Clip(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
    {
        return new ClipBuilder(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());
    }

    public static WatermarkBuilder Watermark(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
    {
        return new WatermarkBuilder(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());
    }

    public static ConcatBuilder Concat(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
    {
        return new ConcatBuilder(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());
    }

    public static FilterGraphBuilder Filters(FFmpegCommandBuilder commandBuilder)
    {
        return new FilterGraphBuilder(commandBuilder);
    }

    // ───────────────────────────────────────────────
    // Utility / Advanced
    // ───────────────────────────────────────────────

    /// <summary>
    /// Creates a fresh command builder (low-level access).
    /// </summary>
    public static FFmpegCommandBuilder Command(
        FFmpegOptions? options = null)
    {
        return new FFmpegCommandBuilder(options ?? GlobalOptions.Clone());
    }

    /// <summary>
    /// Manually set or replace the default runner (e.g. for DI or custom locator).
    /// </summary>
    public static void SetDefaultRunner(IFFmpegRunner runner)
    {
        _defaultRunner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <summary>
    /// Set global options once (e.g. in Startup/Program.cs).
    /// </summary>
    public static void Configure(Action<FFmpegOptions> configure)
    {
        var opts = new FFmpegOptions();
        configure(opts);
        GlobalOptions = opts;
    }

    // Helper to get logger (fallback to null if no DI)
    private static ILogger<T>? GetLogger<T>()
    {
        // In real app → resolve from IServiceProvider
        // For now → return null (no logging by default)
        return null;
    }
}
