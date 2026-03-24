using ManuHub.FF.NET.Abstractions;
using ManuHub.FF.NET.Parsing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
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
        string ffmpegPath = await GetFFmpegPathAsync(options, cancellationToken).ConfigureAwait(false);
        return await RunProcessAsync(ffmpegPath, arguments, options, progress, knownDuration, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProcessResult> RunFFprobeAsync(string[] arguments,
                                                     FFmpegOptions options,
                                                     TimeSpan? knownDuration = null,
                                                     CancellationToken cancellationToken = default)
    {
        string ffprobePath = await GetFFprobePathAsync(options, cancellationToken).ConfigureAwait(false);
        return await RunProcessAsync(ffprobePath, arguments, options, progress: null, knownDuration, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetFFmpegPathAsync(FFmpegOptions options, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.FFmpegPath) && File.Exists(options.FFmpegPath))
            return options.FFmpegPath;

        string? path = await _binaryLocator.LocateFFmpegAsync(ct).ConfigureAwait(false);
        if (path is null)
            throw new FileNotFoundException("ffmpeg executable could not be found. Please specify FFmpegPath in options or install ffmpeg.");

        return path;
    }

    private async Task<string> GetFFprobePathAsync(FFmpegOptions options, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.FFprobePath) && File.Exists(options.FFprobePath))
            return options.FFprobePath;

        string? path = await _binaryLocator.LocateFFprobeAsync(ct).ConfigureAwait(false);
        if (path is null)
            throw new FileNotFoundException("ffprobe executable could not be found.");

        return path;
    }

    private async Task<ProcessResult> RunProcessAsync(string executable,
                                                      string[] args,
                                                      FFmpegOptions options,
                                                      IProgress<FFmpegProgress>? progress,
                                                      TimeSpan? knownDuration = null,
                                                      CancellationToken ct = default)
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
            StandardErrorEncoding = Encoding.UTF8,
        };

        options.Logger?.LogDebug("Executing: {Executable} {Arguments}", executable, psi.Arguments);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdOutBuilder = new StringBuilder(16 * 1024);
        var stdErrBuilder = new StringBuilder(64 * 1024);

        var progressParser = new ProgressParser(totalDuration: knownDuration, progress: progress);

        var startTime = DateTime.UtcNow;

        try
        {
            process.Start();

            var stdErrReader = Task.Run(async () =>
            {
                using var reader = process.StandardError;
                var buffer = new char[1024];
                var sb = new StringBuilder();

                while (true)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    int read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read == 0)
                        break; // EOF

                    sb.Append(buffer, 0, read);

                    // split on \r or \n
                    string data = sb.ToString();
                    string[] lines = data.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    // keep last incomplete line in buffer
                    sb.Clear();
                    if (!data.EndsWith("\n") && !data.EndsWith("\r") && lines.Length > 0)
                    {
                        sb.Append(lines[^1]);
                        lines = lines[..^1];
                    }

                    foreach (var line in lines)
                    {
                        Console.WriteLine(line);              // for debugging
                        progressParser.Feed(line + "\n");     // feed parser
                    }
                }

                progressParser.Flush();
            }, ct);

            var stdOutReader = Task.Run(async () =>
            {
                using var reader = process.StandardOutput;
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
                {
                    stdOutBuilder.AppendLine(line);
                    options.Logger?.LogTrace("[ffmpeg stdout] {Line}", line);
                }
            }, ct);

            await Task.WhenAll(process.WaitForExitAsync(cts.Token), stdErrReader, stdOutReader).ConfigureAwait(false);

            return new ProcessResult(process.ExitCode, stdOutBuilder.ToString(), stdErrBuilder.ToString(), DateTime.UtcNow - startTime);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* ignore */ }
            throw new OperationCanceledException("FFmpeg process was cancelled or timed out.", ct);
        }
        finally
        {
            process.Dispose();
        }
    }
}