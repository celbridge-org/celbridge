namespace Celbridge.UserInterface.Services;

/// <summary>
/// Returns native key focus to the host window itself, so no hosted web surface holds it and managed
/// panels receive keys through the platform's normal routing.
/// </summary>
public interface IHostWindowFocus
{
    /// <summary>
    /// Makes the host window the native keyboard focus target. On macOS this makes the window content
    /// view the first responder; a no-op on heads where hosted web views participate in managed focus.
    /// </summary>
    void FocusHostWindow();
}
