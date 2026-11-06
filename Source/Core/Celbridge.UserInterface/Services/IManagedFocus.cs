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
    /// Records which element has managed keyboard focus, so a caller can later check whether it still has focus,
    /// or give focus back to it after a modal dialog or anything else that took the keyboard has gone. If focus is
    /// in an open menu, this records the element that had focus before the menu opened. The focused item goes when the
    /// menu closes.
    /// </summary>
    INotedFocus NoteFocus();

    /// <summary>
    /// Gives up managed keyboard focus by moving it onto an inert placeholder. The control that had focus then
    /// stops receiving keys. Does nothing if managed focus is already on the placeholder. A caller about to
    /// remove the focused element calls this first. If the caller skips this, WinUI hands focus to the next tab
    /// stop when the element goes. That tab stop can be a hosted web view. The application would then read the
    /// web view's focus report as a move the user made.
    /// </summary>
    void YieldFocus();
}
