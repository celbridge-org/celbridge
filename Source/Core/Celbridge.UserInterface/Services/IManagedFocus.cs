namespace Celbridge.UserInterface.Services;

/// <summary>
/// The managed keyboard focus of the window: whether a popup currently holds it, and the means to give it
/// up so no managed control claims keys destined for a focused web surface.
/// </summary>
public interface IManagedFocus
{
    /// <summary>
    /// True while managed keyboard focus rests inside a popup: a flyout, a context menu or a content
    /// dialog. Answered from the focused element each time it is asked, so it cannot go stale.
    /// </summary>
    bool IsPopupHoldingFocus { get; }

    /// <summary>
    /// Gives up managed keyboard focus, so the keys the platform routes through the managed tree reach no
    /// control. A no-op on heads where hosted web views participate in managed focus, and when managed
    /// focus has already been given up.
    /// </summary>
    void Yield();
}
