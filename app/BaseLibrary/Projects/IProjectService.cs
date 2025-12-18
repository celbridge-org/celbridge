using Windows.Foundation;

namespace Celbridge.Projects;

/// <summary>
/// Provides services for managing projects.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Returns the current loaded project.
    /// </summary>
    IProject? CurrentProject { get; }

    /// <summary>
    /// Check if a new project config is valid.
    /// </summary>
    Result ValidateNewProjectConfig(NewProjectConfig config);

    /// <summary>
    /// Create a new project file and database using the specified config information.
    /// </summary>
    Task<Result> CreateProjectAsync(NewProjectConfig config);

    /// <summary>
    /// Load the project file at the specified path with migration support.
    /// </summary>
    Task<Result<IProject>> LoadProjectAsync(string projectFilePath);

    /// <summary>
    /// Unload the current loaded project.
    /// </summary>
    Task<Result> UnloadProjectAsync();

    /// <summary>
    /// Register our handler for rebuilding the Shortcuts in the UI.
    /// </summary>
    void RegisterRebuildShortcutsUI(TypedEventHandler<IProjectService, RebuildShortcutsUIEventArgs> handler);

    /// <summary>
    /// Unregister our handler for rebuilding the Shortcuts in the UI.
    /// </summary>
    void UnregisterRebuildShortcutsUI(TypedEventHandler<IProjectService, RebuildShortcutsUIEventArgs> handler);

    /// <summary>
    /// Event Arguments for RebuildShortcutsUI event.
    /// </summary>
    public class RebuildShortcutsUIEventArgs : EventArgs
    {
        // Storage for our Navigation Bar Section information from the configuration.
        public NavigationBarSection NavigationBarSection { get; set; } = new();
    }

    /// <summary>
    /// Call to invoke our handler for rebuilding the Shortcuts in the UI.
    /// </summary>
    void InvokeRebuildShortcutsUI(NavigationBarSection navigationBarSection);
}
