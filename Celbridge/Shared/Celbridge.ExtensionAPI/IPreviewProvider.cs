using Celbridge.Foundation;

namespace Celbridge.ExtensionAPI;

/// <summary>
/// Provides a HTML preview version of text data in a specific language (e.g. Markdown)
/// </summary>
public interface IPreviewProvider
{
    /// <summary>
    /// Generates a HTML preview of the specified text.
    /// </summary>
    Task<Result<string>> GeneratePreviewHtml(string text);
}
