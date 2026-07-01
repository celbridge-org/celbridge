namespace Celbridge.UserInterface.Services;

/// <summary>
/// Hosts the application toolbar in the main page layout the way each platform expects, and returns the
/// hosted title bar.
/// </summary>
internal interface IApplicationToolbarHost
{
    /// <summary>
    /// Installs the toolbar host into the layout and returns the title bar to register.
    /// </summary>
    ITitleBar Install(Window window, Panel layoutRoot);
}
