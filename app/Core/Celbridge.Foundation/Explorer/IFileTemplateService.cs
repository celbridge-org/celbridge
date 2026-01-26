namespace Celbridge.Explorer;

/// <summary>
/// Provides initial content for new files based on their file type.
/// </summary>
public interface IFileTemplateService
{
    /// <summary>
    /// Returns the initial content for a new file based on its file extension.
    /// </summary>
    byte[] GetNewFileContent(string filePath);
}
