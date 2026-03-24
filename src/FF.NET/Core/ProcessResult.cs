namespace ManuHub.FF.NET.Core;

/// <summary>
/// Immutable result of running an external process (ffmpeg, ffprobe, etc.)
/// </summary>
public sealed class ProcessResult
{
    /// <summary>
    /// Exit code of the process (0 = success)
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Full standard output content (usually empty or minimal when progress parsing is used)
    /// </summary>
    public string StandardOutput { get; }

    /// <summary>
    /// Full standard error content (very important for ffmpeg – contains most useful messages)
    /// </summary>
    public string StandardError { get; }

    /// <summary>
    /// Wall-clock time the process actually ran
    /// </summary>
    public TimeSpan ExecutionDuration { get; }

    /// <summary>
    /// Whether the process completed successfully (ExitCode == 0)
    /// </summary>
    public bool Success => ExitCode == 0;

    public ProcessResult(
        int exitCode,
        string standardOutput,
        string standardError,
        TimeSpan executionDuration)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput ?? "";
        StandardError = standardError ?? "";
        ExecutionDuration = executionDuration;
    }

    public override string ToString()
    {
        return $"ProcessResult (Exit:{ExitCode}, Success:{Success}, Duration:{ExecutionDuration.TotalSeconds:F2}s)";
    }
}