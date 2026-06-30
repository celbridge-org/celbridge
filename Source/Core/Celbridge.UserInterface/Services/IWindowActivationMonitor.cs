namespace Celbridge.UserInterface.Services;

/// <summary>
/// Watches the main window's activation state and broadcasts activation changes. Drives the custom title
/// bar's active/inactive tint on heads that draw one.
/// </summary>
public interface IWindowActivationMonitor
{
    /// <summary>
    /// Starts monitoring the given window's activation state.
    /// </summary>
    void Start(Window window);
}
