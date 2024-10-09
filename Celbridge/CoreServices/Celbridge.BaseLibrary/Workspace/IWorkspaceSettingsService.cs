using Celbridge.Foundation;

namespace Celbridge.Workspace;

/// <summary>
/// Manages workspace settings associated with the current loaded project.
/// </summary>
public interface IWorkspaceSettingsService
{
    /// <summary>
    /// Returns the workspace settings for the current loaded project.
    /// </summary>
    IWorkspaceSettings? WorkspaceSettings { get; }
    
    /// <summary>
    /// Folder containing the workspace settings database
    /// </summary>
    string? WorkspaceSettingsFolderPath { get; set; }

    /// <summary>
    /// Loads the workspace settings database associated with the current loaded project.
    /// Creates the workspace settings database if it doesn't exist.
    /// </summary>
    Task<Result> AcquireWorkspaceSettingsAsync();

    /// <summary>
    /// Creates a workspace settings database at the specified path.
    /// </summary>
    Task<Result> CreateWorkspaceSettingsAsync(string databasePath);

    /// <summary>
    /// Load the workspace settings database at the specified path.
    /// </summary>
    Result LoadWorkspaceSettings(string databasePath);

    /// <summary>
    /// Unloads the currently loaded workspace settings database.
    /// </summary>
    Result UnloadWorkspaceSettings();
}
