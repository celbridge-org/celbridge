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
    /// Folder containing Python logs.
    /// </summary>
    public const string LogsFolder = ".logs";

    /// <summary>
    /// Folder containing soft-deleted files for undo support.
    /// </summary>
    public const string TrashFolder = ".trash";

    /// <summary>
    /// Folder containing in-flight temp files for atomic writes.
    /// Wiped on workspace load to clear orphans left by previous crashes.
    /// </summary>
    public const string TempFolder = ".temp";

    /// <summary>
    /// File containing the workspace settings data.
    /// </summary>
    public const string WorkspaceSettingsFile = "workspace_settings.db";

    /// <summary>
    /// Hidden folder that contains all Celbridge-internal storage in the new layout.
    /// </summary>
    public const string CelbridgeFolder = ".celbridge";

    /// <summary>
    /// Sub-folder of .celbridge/ that backs the temp: virtual root.
    /// </summary>
    public const string CelbridgeTempFolder = "temp";

    /// <summary>
    /// Sub-folder of .celbridge/ that backs the logs: virtual root.
    /// </summary>
    public const string CelbridgeLogsFolder = "logs";

    /// <summary>
    /// Sub-folder of .celbridge/ for soft-deleted files. Cleared on every workspace load.
    /// </summary>
    public const string CelbridgeTrashFolder = "trash";

    /// <summary>
    /// Sub-folder of .celbridge/ that stages in-flight temp files for atomic
    /// writes performed by the resource file-system chokepoint. Wiped on
    /// workspace load to clear orphans left by previous crashes.
    /// </summary>
    public const string CelbridgeStagingFsFolder = "staging-fs";

    /// <summary>
    /// Sub-folder of .celbridge/ that holds host-private caches. Files in this
    /// folder are managed directly by their owning services (e.g. the metadata
    /// cache) rather than through IResourceFileSystem.
    /// </summary>
    public const string CelbridgeCacheFolder = "cache";

    /// <summary>
    /// Filename of the resource-metadata cache inside CelbridgeCacheFolder.
    /// JSON document; mtime + size validated per-entry on load.
    /// </summary>
    public const string MetaDataCacheFileName = "metadata.json";
}
