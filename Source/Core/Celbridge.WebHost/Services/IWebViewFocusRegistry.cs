using Celbridge.Workspace;

namespace Celbridge.WebHost;

/// <summary>
/// How a web view takes part in focus tracking. Panel and EditTarget go with each focus report. ReleaseFocus
/// drops the page's caret when focus leaves the view. GrantDomFocus, if set, places the caret after the view
/// takes native focus. OnFocusGained, if set, runs when the view gains focus.
/// </summary>
public sealed record WebViewFocusContext(
    FocusPanelId Panel,
    IEditTarget EditTarget,
    Action ReleaseFocus,
    Func<Task>? GrantDomFocus = null,
    Action? OnFocusGained = null);

/// <summary>
/// The single integration point for hosted web-surface focus on the Skia heads, where WebView and host focus
/// are not integrated. One registration per surface replaces the per-view GotFocus, native-monitor, and grant
/// wiring: the registry owns those signals and reports each surface's focus to the focus service.
/// </summary>
public interface IWebViewFocusRegistry
{
    /// <summary>
    /// Registers a web view under the given focus context and starts tracking its focus. Registering a view again
    /// replaces its registration. The focus service treats the new registration as a new surface. When the view
    /// closes, the registry drops it and clears its edit target if that target is still current.
    /// </summary>
    void Register(IWebView view, WebViewFocusContext focusContext);

    /// <summary>
    /// Gives the view keyboard focus (native first responder on macOS, managed focus on Windows), applies its
    /// optional DOM-side focus, and reports the focus. Used by tab clicks, document opens, the console title
    /// bar, the find bar, and layout-mode changes. A view that has not registered yet takes focus as soon as
    /// it registers, unless a later grant supersedes it.
    /// </summary>
    void GrantFocus(IWebView view);

    /// <summary>
    /// Whether the element is a web surface registered here. The registry reports a web surface's focus
    /// itself, carrying the release callback that a report classified from the visual tree cannot supply, so
    /// an observer of managed focus asks this before reporting an element as its own claim.
    /// </summary>
    bool IsRegisteredWebSurface(DependencyObject element);

    /// <summary>
    /// Whether a hosted web surface's focus report is current. The reconciler derives the desired focus
    /// state from this.
    /// </summary>
    bool HasFocusedSurface { get; }

    /// <summary>
    /// Whether the given web view is the hosted surface whose focus report is current.
    /// </summary>
    bool IsFocusedSurface(IWebView view);

    /// <summary>
    /// Whether the pointer press being dispatched landed on a registered web surface. The native click
    /// monitor answers before the managed pointer pipeline raises the press, so a managed handler can tell a
    /// press meant for a page from one meant for the managed tree. False on heads without that monitor.
    /// </summary>
    bool IsPressOnWebSurface { get; }

    /// <summary>
    /// Makes the focused surface's web view the platform keyboard focus target, with no focus report and
    /// no DOM-side caret change (the page's caret stays exactly where the user put it). The reconciler's
    /// native apply step; a no-op when no hosted surface holds focus.
    /// </summary>
    void FocusFocusedSurface();

    /// <summary>
    /// Delivers a native key event to the focused surface's web view, bypassing the managed pipeline.
    /// Used for editing-command keys (Backspace, Enter, the arrows) that the platform routes into its own
    /// command handling instead of the first responder while a hosted surface holds focus. Returns false
    /// when no hosted surface holds focus. It also returns false when a web view has already received the
    /// event, so a page never gets the same key twice. macOS-only, like TryHandleTabKey.
    /// </summary>
    bool TryForwardKeyEvent(IntPtr nativeKeyEvent);

    /// <summary>
    /// Handles a Tab or Shift+Tab press while a hosted surface holds focus. The focused surface's edit target
    /// acts on it first, and if it does not take the key the key goes to the native web view instead. Returns
    /// false when no hosted surface holds focus, letting normal focus navigation proceed. macOS-only.
    /// </summary>
    bool TryHandleTabKey(bool shift, IntPtr nativeKeyEvent);
}
