using Celbridge.Settings;

namespace Celbridge.Workspace;

/// <summary>
/// Manages workspace settings associated with the current loaded project.
/// </summary>
public interface IWorkspaceSettingsService
{
    /// <summary>
    /// Returns the dynamic workspace property bag for the current loaded project.
    /// </summary>
    IWorkspacePropertyBag? PropertyBag { get; }

    /// <summary>
    /// The key/value store backing Workspace-scope settings for the
    /// current loaded project. Null when no workspace is loaded.
    /// </summary>
    ISettingsStore? WorkspaceSettingsStore { get; }

    /// <summary>
    /// Folder containing the workspace settings file.
    /// </summary>
    string? WorkspaceSettingsFolderPath { get; set; }

    /// <summary>
    /// Loads the workspace settings for the current loaded project, creating the
    /// settings file if it does not exist. Idempotent: a no-op when already loaded.
    /// </summary>
    Task<Result> AcquireWorkspaceSettingsAsync();

    /// <summary>
    /// Unloads the currently loaded workspace settings.
    /// </summary>
    Result UnloadWorkspaceSettings();
}
