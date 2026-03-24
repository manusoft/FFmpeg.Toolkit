using ManuHub.FF.NET.Core;

namespace ManuHub.FF.NET;

/// <summary>
/// High-level result of an FFmpeg operation (conversion, extraction, etc.).
/// Provides success/failure status, useful metadata, parsed progress summary,
/// and easy access to errors or output files.
/// </summary>
public sealed class FFmpegResult
{
    /// <summary>
    /// Whether the FFmpeg process completed successfully (exit code 0 and no fatal errors detected).
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Exit code returned by the FFmpeg process (0 = success).
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Wall-clock execution time of the FFmpeg process.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Path to the primary output file (if applicable, e.g. converted video).
    /// May be null for operations that produce multiple files or no file output.
    /// </summary>
    public string? OutputFilePath { get; }

    /// <summary>
    /// Final progress state (last reported progress before completion).
    /// Useful for logging final time, speed, frames processed, etc.
    /// </summary>
    public FFmpegProgress? FinalProgress { get; }

    /// <summary>
    /// Full standard error output from FFmpeg (raw text).
    /// Contains detailed error messages, warnings, filter logs, etc.
    /// </summary>
    public string StandardError { get; }

    /// <summary>
    /// Full standard output (usually minimal when using -progress).
    /// </summary>
    public string StandardOutput { get; }

    /// <summary>
    /// If the operation failed, a human-readable reason (parsed from stderr or exit code).
    /// Null on success.
    /// </summary>
    public string? FailureReason { get; }

    /// <summary>
    /// Exception that caused cancellation or internal failure (if any).
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Whether the operation was cancelled (via CancellationToken or timeout).
    /// </summary>
    public bool IsCancelled { get; }

    /// <summary>
    /// Constructor for successful results.
    /// </summary>
    private FFmpegResult(bool success,
                         int exitCode,
                         TimeSpan duration,
                         string? outputFilePath,
                         FFmpegProgress? finalProgress,
                         string stdOut,
                         string stdErr,
                         string? failureReason,
                         Exception? exception,
                         bool isCancelled)
    {
        Success = success;
        ExitCode = exitCode;
        Duration = duration;
        OutputFilePath = outputFilePath;
        FinalProgress = finalProgress;
        StandardOutput = stdOut ?? "";
        StandardError = stdErr ?? "";
        FailureReason = failureReason;
        Exception = exception;
        IsCancelled = isCancelled;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static FFmpegResult SuccessResult(string? outputFilePath,
                                             FFmpegProgress? finalProgress,
                                             ProcessResult processResult)
    {
        return new FFmpegResult(success: true,
                                exitCode: processResult.ExitCode,
                                duration: processResult.ExecutionDuration,
                                outputFilePath: outputFilePath,
                                finalProgress: finalProgress,
                                stdOut: processResult.StandardOutput,
                                stdErr: processResult.StandardError,
                                failureReason: null,
                                exception: null,
                                isCancelled: false);
    }

    /// <summary>
    /// Creates a failed result from process execution.
    /// </summary>
    public static FFmpegResult FailureFromProcess(ProcessResult processResult,
                                                  FFmpegProgress? lastProgress = null,
                                                  string? outputFilePath = null)
    {
        string? reason = ExtractFailureReason(processResult.StandardError, processResult.ExitCode);

        return new FFmpegResult(success: false,
                                exitCode: processResult.ExitCode,
                                duration: processResult.ExecutionDuration,
                                outputFilePath: outputFilePath,
                                finalProgress: lastProgress,
                                stdOut: processResult.StandardOutput,
                                stdErr: processResult.StandardError,
                                failureReason: reason ?? $"FFmpeg exited with code {processResult.ExitCode}",
                                exception: null,
                                isCancelled: false);
    }

    /// <summary>
    /// Creates a result for cancelled/timed-out operations.
    /// </summary>
    public static FFmpegResult CancelledResult(TimeSpan durationSoFar,
                                               FFmpegProgress? lastProgress = null,
                                               Exception? exception = null)
    {
        return new FFmpegResult(success: false,
                                exitCode: -1, // conventional for cancelled
                                duration: durationSoFar,
                                outputFilePath: null,
                                finalProgress: lastProgress,
                                stdOut: "",
                                stdErr: "",
                                failureReason: "Operation was cancelled or timed out",
                                exception: exception,
                                isCancelled: true);
    }

    /// <summary>
    /// Creates a result for internal library exceptions (e.g. file not found, invalid arguments).
    /// </summary>
    public static FFmpegResult FromException(Exception ex,
                                             string? outputFilePath = null,
                                             FFmpegProgress? lastProgress = null)
    {
        return new FFmpegResult(success: false,
                                exitCode: -2,
                                duration: TimeSpan.Zero,
                                outputFilePath: outputFilePath,
                                finalProgress: lastProgress,
                                stdOut: "",
                                stdErr: ex.Message,
                                failureReason: ex.Message,
                                exception: ex,
                                isCancelled: ex is OperationCanceledException);
    }

    private static string? ExtractFailureReason(string stderr, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return null;

        string[] lines = stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // Common patterns – ordered by likelihood
        foreach (string line in lines)
        {
            string trimmed = line.Trim();

            if (trimmed.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase))
                return "Input file not found or inaccessible";

            if (trimmed.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase))
                return "Invalid or corrupted input file";

            if (trimmed.Contains("Unable to find a suitable output format", StringComparison.OrdinalIgnoreCase))
                return "Unsupported or invalid output format";

            if (trimmed.Contains("Encoder not found", StringComparison.OrdinalIgnoreCase))
                return "Requested codec/encoder not available in this FFmpeg build";

            if (trimmed.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
                return "Permission denied writing to output path";

            if (trimmed.StartsWith("Error opening", StringComparison.OrdinalIgnoreCase))
                return "Failed to open input or output file";
        }

        // Fallback
        return exitCode switch
        {
            1 => "General FFmpeg error (check logs)",
            2 => "Invalid command-line arguments",
            _ => null
        };
    }

    public override string ToString()
    {
        if (Success)
        {
            string prog = FinalProgress?.ToString() ?? "No progress data";
            return $"Success | Duration: {Duration.TotalSeconds:F1}s | Output: {OutputFilePath ?? "n/a"} | {prog}";
        }

        string reason = IsCancelled ? "Cancelled" : FailureReason ?? $"Exit code {ExitCode}";
        return $"Failed | {reason} | Duration: {Duration.TotalSeconds:F1}s | {Exception?.GetType().Name ?? ""}";
    }
}