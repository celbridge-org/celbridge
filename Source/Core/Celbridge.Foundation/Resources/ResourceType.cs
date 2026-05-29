namespace Celbridge.Resources;

/// <summary>
/// The kind of a resource registered with the project.
/// </summary>
public enum ResourceType
{
    /// <summary>
    /// Sentinel value used when no resource has been resolved yet, or when
    /// resolution failed.
    /// </summary>
    Invalid,

    /// <summary>
    /// A file resource.
    /// </summary>
    File,

    /// <summary>
    /// A folder resource.
    /// </summary>
    Folder,
}
