namespace Celbridge.Workspace;

/// <summary>
/// Provides services for managing workspace databases.
/// </summary>
public interface IWorkspaceDataService
{
    /// <summary>
    /// Returns the currently loaded workspace data.
    /// </summary>
    IWorkspaceData? LoadedWorkspaceData { get; }
    
    /// <summary>
    /// Folder containing the workspace database
    /// </summary>
    string? DatabaseFolder { get; set; }

    /// <summary>
    /// Loads the workspace database associated with the loaded project database.
    /// Creates the workspace database if it doesn't exist.
    /// </summary>
    /// <returns></returns>
    Task<Result> AcquireWorkspaceDataAsync();

    /// <summary>
    /// Creates a workspace database at the specified path.
    /// </summary>
    Task<Result> CreateWorkspaceDataAsync(string databasePath);

    /// <summary>
    /// Load the workspace database at the specified path.
    /// </summary>
    Result LoadWorkspaceData(string databasePath);

    /// <summary>
    /// Unloads the currently loaded workspace database.
    /// </summary>
    Result UnloadWorkspaceData();
}
