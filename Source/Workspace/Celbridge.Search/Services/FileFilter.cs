using Celbridge.Resources;
using Path = System.IO.Path;

namespace Celbridge.Search;

/// <summary>
/// Determines which files should be included in search operations.
/// </summary>
public class FileFilter
{
    private const int MaxFileSizeBytes = 1024 * 1024; // 1MB

    private readonly ITextBinarySniffer _textBinarySniffer;

    private readonly HashSet<string> _metadataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cel",
        ".celbridge"
    };

    public FileFilter(ITextBinarySniffer textBinarySniffer)
    {
        _textBinarySniffer = textBinarySniffer;
    }

    /// <summary>
    /// Checks if a file should be included in search. Routes the file probe
    /// through the chokepoint so the size check honours the same containment
    /// validation as the read that follows.
    /// </summary>
    public async Task<bool> ShouldSearchFileAsync(IFileStorage fileStorage, ResourceKey resource, string filePath)
    {
        var infoResult = await fileStorage.GetInfoAsync(resource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return false;
        }

        // Skip large files
        if (infoResult.Value.Size > MaxFileSizeBytes)
        {
            return false;
        }

        // Skip excluded file types
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (_metadataExtensions.Contains(extension) || _textBinarySniffer.IsBinaryExtension(extension))
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
        var result = _textBinarySniffer.IsTextFile(filePath);
        return result.IsSuccess && result.Value;
    }

    /// <summary>
    /// Checks if file content appears to be text (not binary).
    /// Uses thorough heuristics including BOM detection, UTF-8 validation,
    /// and control character ratio analysis.
    /// </summary>
    public bool IsTextContent(string content)
    {
        return _textBinarySniffer.IsTextContent(content);
    }
}
