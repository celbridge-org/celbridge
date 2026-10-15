namespace Celbridge.Projects;

/// <summary>
/// Decides which project to open when the app is activated. A project file named by the operating system or the
/// command line opens first. Otherwise the startup flow opens the project from the last session, once the main page
/// has loaded. Each load names how it started, which decides what happens when another instance has the project
/// open.
/// </summary>
public interface IAppActivationService
{
    /// <summary>
    /// Handles the file paths the operating system asked the app to open, or the project file named on the
    /// command line. Defers the project file to the startup flow until startup completes. After startup, the
    /// project file opens immediately.
    /// </summary>
    void OnFilesActivated(IReadOnlyList<string> filePaths);
}
