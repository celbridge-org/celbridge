namespace Celbridge.UserInterface.Services;

/// <summary>
/// Watches the main window and broadcasts a message when it is activated.
/// </summary>
public interface IWindowActivationMonitor
{
    /// <summary>
    /// Starts monitoring the given window's activation state.
    /// </summary>
    void Start(Window window);
}
