namespace Celbridge.UserInterface;

/// <summary>
/// Manages the loading and retrieval of icon definitions.
/// </summary>
public interface IIconService
{
    /// <summary>
    /// Loads the definition data for all supported icons.
    /// </summary>
    Result LoadIconDefinitions();

    /// <summary>
    /// Returns the icon definition for the specified icon name.
    /// </summary>
    Result<IconDefinition> GetIcon(string iconName);

    /// <summary>
    /// Returns the icon definition for the specified file extension.
    /// </summary>
    Result<IconDefinition> GetIconForFileExtension(string fileExtension);

    /// <summary>
    /// Returns the default icon definition for file resources.
    /// </summary>
    IconDefinition DefaultFileIcon { get; }

    /// <summary>
    /// Returns the default icon definition for folder resources.
    /// </summary>
    IconDefinition DefaultFolderIcon { get; }
}
