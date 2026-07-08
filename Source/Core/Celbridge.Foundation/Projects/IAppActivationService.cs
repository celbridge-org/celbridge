namespace Celbridge.Projects;

/// <summary>
/// Decides which project to open when the app is activated: the project file the operating system asked the
/// app to open (such as a double-clicked .celbridge file), or the previously open project at startup. Opens
/// the startup project when the main page has loaded.
/// </summary>
public interface IAppActivationService
{
    /// <summary>
    /// Handles the file paths the operating system asked the app to open. Before startup completes the
    /// project file is deferred to the startup flow; afterwards it opens immediately.
    /// </summary>
    void OnFilesActivated(IReadOnlyList<string> filePaths);
}
