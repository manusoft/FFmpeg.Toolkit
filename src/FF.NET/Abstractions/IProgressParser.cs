namespace ManuHub.FF.NET.Abstractions;

/// <summary>
/// Interface for feeding and parsing FFmpeg progress output.
/// </summary>
public interface IProgressParser
{
    /// <summary>
    /// Feed new data chunk (from stderr or progress pipe).
    /// </summary>
    void Feed(string data);

    /// <summary>
    /// Flush any remaining buffered data at end of stream.
    /// </summary>
    void Flush();
}