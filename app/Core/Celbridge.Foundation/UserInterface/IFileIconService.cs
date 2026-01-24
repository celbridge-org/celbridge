namespace Celbridge.UserInterface;

/// <summary>
/// Information required to display an icon using the FontIcon control.
/// </summary>
public record FileIconDefinition(string FontCharacter, string FontColor, string FontFamily, string FontSize);

/// <summary>
/// Manages the loading and retrieval of file icon definitions.
/// </summary>
public interface IFileIconService
{
    /// <summary>
    /// Loads the definition data for all supported file icons.
    /// </summary>
    Result LoadDefinitions();

    /// <summary>
    /// Returns the file icon definition for the specified icon name.
    /// </summary>
    Result<FileIconDefinition> GetFileIcon(string iconName);

    /// <summary>
    /// Returns the file icon definition for the specified file extension.
    /// </summary>
    Result<FileIconDefinition> GetFileIconForExtension(string fileExtension);

    /// <summary>
    /// Returns the default icon definition for file resources.
    /// </summary>
    FileIconDefinition DefaultFileIcon { get; }

    /// <summary>
    /// Returns the default icon definition for folder resources.
    /// </summary>
    FileIconDefinition DefaultFolderIcon { get; }
}
