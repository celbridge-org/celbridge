namespace Celbridge.Projects;

/// <summary>
/// String constants for the legacy project layout (the user-visible 'celbridge/'
/// meta-data folder and its sub-folders) that predates the '.celbridge/' hidden
/// layout. Retained while the entity service and a handful of cleanup paths
/// still reference the old locations; delete this file when those last
/// consumers move over.
/// </summary>
public static class LegacyConstants
{
    /// <summary>
    /// Legacy user-visible meta-data folder. Replaced by ProjectConstants.CelbridgeFolder.
    /// </summary>
    public const string MetaDataFolder = "celbridge";

    /// <summary>
    /// Sub-folder of celbridge/ containing entity data files.
    /// </summary>
    public const string EntitiesFolder = "entities";

    /// <summary>
    /// Sub-folder of celbridge/ for ephemeral cached state. Workspace settings
    /// and the Python fingerprint have moved into .celbridge/; this constant
    /// is unused at runtime and is kept only so cleanup code can identify
    /// orphan folders left over from earlier versions.
    /// </summary>
    public const string CacheFolder = ".cache";

    /// <summary>
    /// Legacy soft-delete folder under celbridge/. Replaced by
    /// ProjectConstants.TrashFolder under .celbridge/.
    /// </summary>
    public const string TrashFolder = ".trash";

    /// <summary>
    /// Legacy in-flight atomic-write staging folder under celbridge/. Replaced
    /// by ProjectConstants.StagingFsFolder under .celbridge/.
    /// </summary>
    public const string TempFolder = ".temp";
}
