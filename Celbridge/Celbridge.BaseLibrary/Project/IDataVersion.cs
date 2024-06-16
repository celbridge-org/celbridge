namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Manages the data version for project databases to support schema changes.
/// </summary>
public interface IDataVersion
{
    /// <summary>
    /// The version of the project database.
    /// </summary>
    int Version { get; set; }
}
