namespace Celbridge.Projects.Services;

/// <summary>
/// Represents the result of comparing a project version with the application version.
/// </summary>
public enum VersionComparisonState
{
    /// <summary>
    /// The project version matches the application version - no migration needed.
    /// </summary>
    SameVersion,
    
    /// <summary>
    /// The project version is older than the application version - migration needed.
    /// </summary>
    OlderVersion,
    
    /// <summary>
    /// The project version is newer than the application version - cannot open project.
    /// </summary>
    NewerVersion,

    /// <summary>
    /// The project version is invalid - cannot open project.
    /// </summary>
    InvalidVersion
}
