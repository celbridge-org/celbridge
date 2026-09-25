namespace Celbridge.UserInterface.Services;

/// <summary>
/// The element managed keyboard focus rested on when IManagedFocus.NoteFocus was called.
/// </summary>
public interface INotedFocus
{
    /// <summary>
    /// Whether managed keyboard focus rests on the noted element again.
    /// </summary>
    bool IsFocusBack { get; }

    /// <summary>
    /// Gives managed keyboard focus back to the noted element. Returns false when nothing held focus at
    /// the time, or the element has since left the window, or cannot take focus.
    /// </summary>
    bool TryReturnFocus();
}

/// <summary>
/// Managed keyboard focus: the WinUI element holding the window's keyboard focus, as opposed to the native
/// focus a hosted web view takes on macOS. Where it rests, and the means to note it and to give it up.
/// </summary>
public interface IManagedFocus
{
    /// <summary>
    /// The element managed keyboard focus rests on, or null when nothing holds it.
    /// </summary>
    UIElement? FocusedElement { get; }

    /// <summary>
    /// Where managed keyboard focus currently rests. Answered from the focused element each time it is
    /// asked, so it cannot go stale, and reads as the main content while nothing holds focus.
    /// </summary>
    FocusLocation FocusLocation { get; }

    /// <summary>
    /// Notes the element holding managed keyboard focus, so it can later be checked for focus or given it
    /// back, as it must be once a modal dialog or anything else that took the keyboard has gone.
    /// </summary>
    INotedFocus NoteFocus();

    /// <summary>
    /// Gives up managed keyboard focus, so the keys the platform routes through the managed tree reach no
    /// control. A no-op on heads where hosted web views participate in managed focus, and when managed
    /// focus has already been given up.
    /// </summary>
    void YieldFocus();
}
