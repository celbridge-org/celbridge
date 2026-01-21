using Celbridge.Utilities;

namespace Celbridge.Explorer.Services.Search;

/// <summary>
/// Determines which files should be included in search operations.
/// </summary>
public class FileFilter
{
    private const int MaxFileSizeBytes = 1024 * 1024; // 1MB

    private readonly HashSet<string> _metadataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webapp",
        ".celbridge"
    };

    private readonly HashSet<string> _binaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".pdb", ".obj", ".o", ".a", ".lib",
        ".so", ".dylib", ".bin", ".dat",
        ".zip", ".tar", ".gz", ".7z", ".rar", ".bz2",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svg",
        ".mp3", ".wav", ".ogg", ".flac", ".aac",
        ".mp4", ".avi", ".mkv", ".mov", ".webm",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        ".pyc", ".pyo", ".class",
        ".db", ".sqlite", ".sqlite3",
        ".nupkg", ".snupkg",
        ".vsix", ".msi", ".cab"
    };

    /// <summary>
    /// Checks if a file should be included in search based on its path.
    /// </summary>
    public bool ShouldSearchFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        var fileInfo = new FileInfo(filePath);

        // Skip large files
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            return false;
        }

        // Skip excluded file types
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (_metadataExtensions.Contains(extension) || _binaryExtensions.Contains(extension))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if a file is likely a text file by examining its content.
    /// This method reads a sample from the file and uses heuristics to detect binary content.
    /// </summary>
    public bool IsTextFile(string filePath)
    {
        var result = TextBinarySniffer.IsTextFile(filePath);
        return result.IsSuccess && result.Value;
    }

    /// <summary>
    /// Checks if file content appears to be text (not binary).
    /// Uses thorough heuristics including BOM detection, UTF-8 validation, 
    /// and control character ratio analysis.
    /// </summary>
    public bool IsTextContent(string content)
    {
        return TextBinarySniffer.IsTextContent(content);
    }
}
