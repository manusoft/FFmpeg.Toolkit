namespace ManuHub.FF.NET.Utils;

/// <summary>
/// Manages temporary files created during FFmpeg operations (concat lists, filter graphs, extracted subtitles, etc.).
/// Supports automatic cleanup when disposed or when the process exits.
/// </summary>
public sealed class TempFileManager : IDisposable
{
    private readonly string _tempDirectory;
    private readonly bool _cleanOnDispose;
    private readonly List<string> _createdFiles = new();
    private readonly object _lock = new();

    public TempFileManager(FFmpegOptions options)
    {
        _tempDirectory = Path.Combine(
            string.IsNullOrWhiteSpace(options.TempDirectory) ? Path.GetTempPath() : options.TempDirectory,
            "FFmpegToolkit_" + Guid.NewGuid().ToString("N").Substring(0, 8));

        _cleanOnDispose = options.CleanTemporaryFiles;

        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Creates a new temporary file with the given extension and returns its full path.
    /// </summary>
    public string CreateTempFile(string extension = ".tmp")
    {
        if (!extension.StartsWith(".")) extension = "." + extension;

        string path;
        lock (_lock)
        {
            path = Path.Combine(_tempDirectory, Guid.NewGuid().ToString("N") + extension);
            _createdFiles.Add(path);
        }

        // Touch the file to reserve it (optional but helps with race conditions)
        File.WriteAllText(path, string.Empty);

        return path;
    }

    /// <summary>
    /// Creates a temporary text file with the given content and returns its path.
    /// Useful for concat lists, filter_complex scripts, etc.
    /// </summary>
    public string WriteTempTextFile(string content, string extension = ".txt")
    {
        string path = CreateTempFile(extension);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (!_cleanOnDispose) return;

        lock (_lock)
        {
            foreach (var file in _createdFiles)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch
                {
                    // Best effort - ignore locked/deleted files
                }
            }

            try
            {
                if (Directory.Exists(_tempDirectory))
                    Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore
            }

            _createdFiles.Clear();
        }
    }

    ~TempFileManager()
    {
        Dispose();
    }
}