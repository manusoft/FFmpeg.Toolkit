using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Parsing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ManuHub.FF.NET.Core;

/// <inheritdoc />
public class FFmpegProcessRunner : IFFmpegRunner
{
    private readonly IBinaryLocator _binaryLocator;
    private readonly ILogger<FFmpegProcessRunner>? _logger;

    public FFmpegProcessRunner(IBinaryLocator binaryLocator, ILogger<FFmpegProcessRunner>? logger = null)
    {
        _binaryLocator = binaryLocator ?? throw new ArgumentNullException(nameof(binaryLocator));
        _logger = logger;
    }

    public async Task<ProcessResult> RunFFmpegAsync(string[] arguments,
                                                    FFmpegOptions options,
                                                    IProgress<FFmpegProgress>? progress = null,
                                                    TimeSpan? knownDuration = null,
                                                    CancellationToken cancellationToken = default)
    {
        string ffmpegPath = await GetFFmpegPathAsync(options, cancellationToken);

        // Automatically enable structured progress if caller wants progress
        var argsList = new List<string>(arguments);
        if (progress != null && !argsList.Contains("-progress"))
        {
            argsList.Insert(0, "-progress");
            argsList.Insert(1, "pipe:1");
        }

        return await RunProcessAsync(ffmpegPath, argsList.ToArray(), options, progress, knownDuration, cancellationToken);
    }

    public async Task<ProcessResult> RunFFprobeAsync(string[] arguments,
                                                     FFmpegOptions options,
                                                     CancellationToken cancellationToken = default)
    {
        string ffprobePath = await GetFFprobePathAsync(options, cancellationToken);
        return await RunProcessAsync(ffprobePath, arguments, options, null, null, cancellationToken);
    }

    private async Task<string> GetFFmpegPathAsync(FFmpegOptions options, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.FFmpegPath) && File.Exists(options.FFmpegPath))
            return options.FFmpegPath;

        var path = await _binaryLocator.LocateFFmpegAsync(ct);
        return path ?? throw new FileNotFoundException("ffmpeg executable not found. Please set FFmpegPath or install FFmpeg.");
    }

    private async Task<string> GetFFprobePathAsync(FFmpegOptions options, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.FFprobePath) && File.Exists(options.FFprobePath))
            return options.FFprobePath;

        var path = await _binaryLocator.LocateFFprobeAsync(ct);
        return path ?? throw new FileNotFoundException("ffprobe executable not found.");
    }

    private async Task<ProcessResult> RunProcessAsync(string executable,
                                                      string[] args,
                                                      FFmpegOptions options,
                                                      IProgress<FFmpegProgress>? progress,
                                                      TimeSpan? knownDuration,
                                                      CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (options.DefaultTimeout > TimeSpan.Zero)
            cts.CancelAfter(options.DefaultTimeout);

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = string.Join(" ", args),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = options.CreateNoWindow,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        options.Logger?.LogDebug("Executing: {Executable} {Args}", executable, psi.Arguments);

        using var process = new Process { StartInfo = psi };

        var stdOutBuilder = new StringBuilder(32 * 1024);
        var stdErrBuilder = new StringBuilder(128 * 1024);

        var progressParser = new ProgressParser(knownDuration, progress);

        var startTime = DateTime.UtcNow;

        try
        {
            process.Start();

            // === CRITICAL: Read stdout for progress when -progress pipe:1 is used ===
            var stdoutTask = Task.Run(async () =>
            {
                using var reader = process.StandardOutput;
                var buffer = new char[8192];
                int bytesRead;

                while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    string chunk = new string(buffer, 0, bytesRead);
                    stdOutBuilder.Append(chunk);

                    // Feed progress parser from stdout when -progress pipe:1 is active
                    if (progress != null)
                        progressParser.Feed(chunk);
                }
                if (progress != null)
                    progressParser.Flush();
            }, ct);

            // Read stderr (for errors and normal logs)
            var stderrTask = Task.Run(async () =>
            {
                using var reader = process.StandardError;
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
                {
                    stdErrBuilder.AppendLine(line);
                    options.Logger?.LogTrace("[FFmpeg stderr] {Line}", line);
                }
            }, ct);

            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cts.Token));

            return new ProcessResult(
                process.ExitCode,
                stdOutBuilder.ToString(),
                stdErrBuilder.ToString(),
                DateTime.UtcNow - startTime);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new OperationCanceledException("FFmpeg process was cancelled or timed out.", ct);
        }
        finally
        {
            process.Dispose();
        }
    }

    public async Task<TimeSpan?> GetDurationAsync(string input, FFmpegOptions options, CancellationToken ct)
    {
        var args = new[]
        {
        "-v", "error",
        "-show_entries", "format=duration",
        "-of", "default=noprint_wrappers=1:nokey=1",
        input
    };

        var result = await RunFFprobeAsync(args, options, ct);

        if (double.TryParse(result.StandardOutput.Trim(),
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out double seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }
}