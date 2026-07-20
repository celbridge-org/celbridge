namespace Celbridge.ProjectSettings;

/// <summary>
/// The built-in Project Settings utility: the panel surface for viewing and editing the current
/// project's .celbridge settings, such as package activation, editor and utility contributions, and
/// editor associations.
/// </summary>
public interface IProjectSettingsPanel
{
    /// <summary>
    /// Sets keyboard focus to the panel.
    /// </summary>
    void FocusPanel();

    /// <summary>
    /// Re-reads the current project's .celbridge file and rebuilds the displayed sections. Called
    /// when the panel is shown so it reflects the on-disk file, including edits made elsewhere.
    /// </summary>
    void Refresh();
}
