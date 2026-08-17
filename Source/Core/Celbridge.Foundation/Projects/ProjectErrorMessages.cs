namespace Celbridge.Projects;

/// <summary>
/// Project or workspace-scoped error states surfaced in the project-notification banners when a
/// workspace loads.
/// </summary>
public enum ProjectErrorType
{
    /// <summary>
    /// The .celbridge project config file doesn't exist or failed to parse.
    /// </summary>
    InvalidProjectConfig,

    /// <summary>
    /// The current version of Celbridge is incompatible with the project version.
    /// This typically indicates that the project was created with a newer version of Celbridge.
    /// </summary>
    IncompatibleVersion,

    /// <summary>
    /// The project version is invalid or not specified.
    /// </summary>
    InvalidVersion,

    /// <summary>
    /// Project migration failed.
    /// </summary>
    MigrationError,

    /// <summary>
    /// One or more packages failed to load during project discovery.
    /// </summary>
    PackageLoadError,

    /// <summary>
    /// The workspace-load project consistency check returned non-empty
    /// findings (broken references, orphan .cel files, or broken .cel files).
    /// </summary>
    ProjectCheckError,

    /// <summary>
    /// One or more entries in the .celbridge project config were skipped or degraded.
    /// The project loads with the remaining entries applied.
    /// </summary>
    ProjectConfigEntryError,
}

/// <summary>
/// Message raised when a project or workspace-scoped error occurs during workspace load, surfaced in
/// the project-notification banners.
/// </summary>
public record ProjectErrorMessage(ProjectErrorType ErrorType, string ConfigFileName)
{
    /// <summary>
    /// How many findings the error covers, for the error types that report a count.
    /// </summary>
    public int FindingCount { get; init; }

    /// <summary>
    /// The report holding the detail behind this error, or null when no report was written.
    /// </summary>
    public ResourceKey? ReportResource { get; init; }
}
