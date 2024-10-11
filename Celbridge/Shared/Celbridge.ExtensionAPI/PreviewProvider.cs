namespace Celbridge.ExtensionAPI;

/// <summary>
/// Provides a HTML preview rendering of text data in a specific language (e.g. markdown, .md files)
/// </summary>
public abstract class PreviewProvider
{
    /// <summary>
    /// The list of supported file extensions.
    /// </summary>
    public List<string> SupportedFileExtensions { get; } = new();

    /// <summary>
    /// Returns true if the preview provider supports the specified file extension.
    /// </summary>
    public bool SupportsFileExtension(string fileExtension)
    {
        return SupportedFileExtensions.Contains(fileExtension);
    }

    /// <summary>
    /// Generates a HTML preview of the specified text.
    /// </summary>
    public abstract Task<Result<string>> GeneratePreview(string text);
}
