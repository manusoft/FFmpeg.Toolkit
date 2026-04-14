using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Builders;
using ManuHub.FF.NET.Core;
using Microsoft.Extensions.Logging;

namespace ManuHub.FF.NET;

/// <summary>
/// Static facade / entry point for the FFmpeg.Toolkit library.
/// Provides factory methods for all major builders with sensible defaults.
/// </summary>
/// <summary>
/// Main static entry point and facade for FFmpeg.Toolkit.
/// Provides easy access to all builders with sensible defaults.
/// </summary>
public static class FFmpegToolkit
{
    private static IFFmpegRunner? _defaultRunner;
    private static FFmpegOptions? _globalOptions;

    /// <summary>
    /// Global default options. Can be configured once at application startup.
    /// </summary>
    public static FFmpegOptions GlobalOptions
    {
        get => _globalOptions ??= new FFmpegOptions();
        set => _globalOptions = value?.Clone() ?? new FFmpegOptions();
    }

    /// <summary>
    /// Lazy-initialized default runner.
    /// </summary>
    public static IFFmpegRunner DefaultRunner
    {
        get
        {
            if (_defaultRunner == null)
            {
                var locator = new DefaultBinaryLocator();
                var logger = GetLogger<FFmpegProcessRunner>();
                _defaultRunner = new FFmpegProcessRunner(locator, logger);
            }
            return _defaultRunner;
        }
    }

    // ───────────────────────────────────────────────
    // Builder Factories
    // ───────────────────────────────────────────────

    public static ProbeBuilder Probe(IFFmpegRunner? runner = null, FFmpegOptions? options = null) 
        => new(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());

    public static ConvertBuilder Convert(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
        => new(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());

    public static GenerateThumbnailsBuilder Thumbnails(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
        => new(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());

    public static ExtractAudioBuilder ExtractAudio(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
        => new(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());

    public static ClipBuilder Clip(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
        => new(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());

    public static WatermarkBuilder Watermark(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
        => new(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());

    public static ConcatBuilder Concat(IFFmpegRunner? runner = null, FFmpegOptions? options = null)
        => new(runner ?? DefaultRunner, options ?? GlobalOptions.Clone());

    public static FilterGraphBuilder Filters(FFmpegCommandBuilder commandBuilder)
        => new(commandBuilder);

    // ───────────────────────────────────────────────
    // Low-level access
    // ───────────────────────────────────────────────

    public static FFmpegCommandBuilder Command(FFmpegOptions? options = null)
        => new(options ?? GlobalOptions.Clone());

    // ───────────────────────────────────────────────
    // Configuration Helpers
    // ───────────────────────────────────────────────

    public static void SetDefaultRunner(IFFmpegRunner runner)
    {
        _defaultRunner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public static void Configure(Action<FFmpegOptions> configure)
    {
        var opts = new FFmpegOptions();
        configure(opts);
        GlobalOptions = opts;
    }

    // ───────────────────────────────────────────────
    // Private Helpers
    // ───────────────────────────────────────────────

    private static ILogger<T>? GetLogger<T>()
    {
        // TODO: In real applications, resolve from DI container (IServiceProvider)
        // For now, logging is disabled by default
        return null;
    }
}
