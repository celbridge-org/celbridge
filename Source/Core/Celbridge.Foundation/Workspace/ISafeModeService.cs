using Celbridge.Documents;

namespace Celbridge.Workspace;

/// <summary>
/// A reason for the workspace to be in Safe Mode.
/// </summary>
public enum SafeModeCause
{
    /// <summary>
    /// The project file failed to load. A failed project upgrade, a configuration error and a project file that
    /// fails to parse each have this cause.
    /// </summary>
    ConfigurationFailed,
}

/// <summary>
/// Keeps the Safe Mode state of the loaded workspace. In Safe Mode, only the editors and Utility Rail buttons on the
/// allow list work, so none of the project's code runs. The state stays fixed until the project reloads.
/// </summary>
public interface ISafeModeService
{
    /// <summary>
    /// Returns true while the workspace is in Safe Mode.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Returns every cause of Safe Mode. The list is empty outside Safe Mode.
    /// </summary>
    IReadOnlyList<SafeModeCause> Causes { get; }

    /// <summary>
    /// Returns true if the editor or Utility Rail button with this id works in the current state. Outside Safe
    /// Mode, every editor and button works.
    /// </summary>
    bool IsAllowed(EditorId editorId);
}
