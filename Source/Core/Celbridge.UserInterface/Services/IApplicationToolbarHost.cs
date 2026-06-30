namespace Celbridge.UserInterface.Services;

/// <summary>
/// Hosts the application toolbar in the main page layout the way each platform expects, and returns the
/// hosted title bar.
/// </summary>
internal interface IApplicationToolbarHost
{
    /// <summary>
    /// Creates the toolbar host, adds it to the layout, wires any window title-bar integration, and returns
    /// it as the title bar to register.
    /// </summary>
    ITitleBar Install(Window window, Panel layoutRoot);
}
