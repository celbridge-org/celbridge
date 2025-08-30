namespace Celbridge.Projects;

/// <summary>
/// Strings constants for project files and folders.
/// </summary>
public static class ProjectConstants
{
    /// <summary>
    /// File extension for Celbridge projects.
    /// </summary>
    public const string ProjectFileExtension = ".celbridge";

    /// <summary>
    /// Folder containing the project meta data.
    /// </summary>
    public const string MetaDataFolder = "celbridge";

    /// <summary>
    /// Folder containing entity data files.
    /// </summary>
    public const string EntitiesFolder = "entities";

    /// <summary>
    /// Folder containing ephemeral cached state, such as workspace settings.
    /// </summary>
    public const string CacheFolder = ".cache";

    /// <summary>
    /// File containing the workspace settings data.
    /// </summary>
    public const string WorkspaceSettingsFile = "workspace_settings.db";
}
