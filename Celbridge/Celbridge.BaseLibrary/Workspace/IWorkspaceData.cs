namespace Celbridge.BaseLibrary.Workspace;

/// <summary>
/// Manages the workspace data associated with a loaded project.
/// </summary>
public interface IWorkspaceData
{
    /// <summary>
    /// Gets the data version for the workspace data.
    /// </summary>
    Task<Result<int>> GetDataVersionAsync();

    /// <summary>
    /// Sets the data version for the workspace data.
    /// </summary>
    Task<Result> SetDataVersionAsync(int version);

    /// <summary>
    /// Returns a list of the expanded folders in the Resource Tree View.
    /// </summary>
    Task<Result<List<string>>> GetExpandedFoldersAsync();

    /// <summary>
    /// Expands the specified folders in the Resource Tree View.
    /// </summary>
    Task<Result> SetExpandedFoldersAsync(List<string> folderNames);
}
