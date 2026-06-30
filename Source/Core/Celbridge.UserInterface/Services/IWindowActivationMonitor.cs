namespace Celbridge.UserInterface.Services;

/// <summary>
/// Watches the main window's activation state and broadcasts activation changes.
/// </summary>
public interface IWindowActivationMonitor
{
    /// <summary>
    /// Starts monitoring the given window's activation state.
    /// </summary>
    void Start(Window window);
}
