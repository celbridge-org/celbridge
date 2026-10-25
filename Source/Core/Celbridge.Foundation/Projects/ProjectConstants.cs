namespace Celbridge.Projects;

/// <summary>
/// String constants for project files and folders.
/// </summary>
public static class ProjectConstants
{
    /// <summary>
    /// File extension for Celbridge projects.
    /// </summary>
    public const string ProjectFileExtension = ".celbridge";

    /// <summary>
    /// Oldest Celbridge version whose projects this build can open. A project whose celbridge-version is
    /// below it is rejected rather than upgraded, because no migration steps span the gap to it.
    /// </summary>
    public const string MinimumSupportedCelbridgeVersion = "1.0.0";

    /// <summary>
    /// File containing the workspace settings data.
    /// </summary>
    public const string WorkspaceSettingsFile = "workspace_settings.json";

    /// <summary>
    /// Git's metadata folder. Celbridge reserves this folder, so resources inside it can't be read or written.
    /// </summary>
    public const string GitFolder = ".git";

    /// <summary>
    /// Sub-folder of the project data folder that backs the temp: virtual root. Wiped when the workspace
    /// loads and when it closes.
    /// </summary>
    public const string TempFolder = "temp";

    /// <summary>
    /// Sub-folder of the project data folder that backs the logs: virtual root.
    /// </summary>
    public const string LogsFolder = "logs";

    /// <summary>
    /// Sub-folder of the project data folder that backs the utils: virtual root. Contains the persistent
    /// state of utility documents. Not wiped on workspace load.
    /// </summary>
    public const string UtilsFolder = "utils";

    /// <summary>
    /// Sub-folder of the project data folder for soft-deleted files. Cleared on every workspace load.
    /// </summary>
    public const string TrashFolder = "trash";

    /// <summary>
    /// Sub-folder of the project data folder that contains the project's own Python state: its IPython
    /// profile and any uv tools the user installs for the project.
    /// </summary>
    public const string PythonFolder = "python";

    /// <summary>
    /// Sub-folder of the project data folder that contains the workspace settings database.
    /// </summary>
    public const string SettingsFolder = "settings";

    /// <summary>
    /// Folder name used for WebView downloads. Used both for the in-progress
    /// staging folder under temp: and for the default destination folder under
    /// project:. A project's [celbridge.downloads] folder key can change the destination.
    /// </summary>
    public const string DownloadsFolder = "downloads";
}
