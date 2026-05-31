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
    /// File containing the workspace settings data.
    /// </summary>
    public const string WorkspaceSettingsFile = "workspace_settings.db";

    /// <summary>
    /// Hidden folder for Celbridge-internal storage.
    /// </summary>
    public const string CelbridgeFolder = ".celbridge";

    /// <summary>
    /// Sub-folder of .celbridge/ that backs the temp: virtual root. Wiped on
    /// workspace load; consumers needing persistence write under project:.
    /// </summary>
    public const string TempFolder = "temp";

    /// <summary>
    /// Sub-folder of .celbridge/ that backs the logs: virtual root.
    /// </summary>
    public const string LogsFolder = "logs";

    /// <summary>
    /// Sub-folder of .celbridge/ for soft-deleted files. Cleared on every workspace load.
    /// </summary>
    public const string TrashFolder = "trash";

    /// <summary>
    /// Sub-folder of .celbridge/ that holds the Python fingerprint and the
    /// IPython profile data.
    /// </summary>
    public const string PythonFolder = "python";

    /// <summary>
    /// Sub-folder of .celbridge/ that holds the workspace settings database.
    /// </summary>
    public const string SettingsFolder = "settings";

    /// <summary>
    /// Folder name used for WebView downloads. Used both for the in-progress
    /// staging folder under temp: and for the destination folder under project:.
    /// </summary>
    public const string DownloadsFolder = "downloads";
}
