namespace Celbridge.Utilities;

/// <summary>
/// Provides heuristic detection of whether a file or content contains text or binary data.
/// Handles UTF-8, UTF-16, UTF-32, and legacy 8-bit text encodings.
/// </summary>
public interface ITextBinarySniffer
{
    /// <summary>
    /// Quickly checks if a file extension indicates a binary file format.
    /// This is a fast path that avoids reading file content.
    /// </summary>
    bool IsBinaryExtension(string extension);

    /// <summary>
    /// Determines if a file is likely a text file by examining its content.
    /// </summary>
    Result<bool> IsTextFile(string path);

    /// <summary>
    /// Determines if the provided content appears to be text (not binary).
    /// </summary>
    bool IsTextContent(string content);
}
