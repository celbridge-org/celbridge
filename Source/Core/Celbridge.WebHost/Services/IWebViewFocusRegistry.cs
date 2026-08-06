using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// A hosted web surface's complete focus contract, supplied once at registration. The registry converges the
/// surface's focus-gain signals (managed GotFocus on Windows, the macOS native click monitor) onto a single
/// report of its Panel, EditTarget, and ReleaseFocus. EditTarget is null for a surface
/// that hosts none (an external .webview document). ReleaseFocus drops the surface's DOM caret when focus leaves
/// it (the JS blur). GrantDomFocus is the optional DOM-side focus the grant path applies after native focus (the
/// console focuses its terminal input; document editors have none yet). OnFocusGained is an optional side effect
/// run when the surface gains focus (a document reports itself as the active document).
/// </summary>
public sealed record WebViewFocusRegistration(
    WebView2 WebView,
    WorkspacePanel Panel,
    IEditTarget? EditTarget,
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
    /// Registers a web surface and begins observing its managed GotFocus and native click focus. Registering a
    /// surface whose CoreWebView2 is already registered replaces the previous registration.
    /// </summary>
    void Register(WebViewFocusRegistration registration);

    /// <summary>
    /// Stops observing the surface and, if its edit target is still the current one, clears it so a torn-down
    /// editor stops receiving Edit commands. Safe to call for a surface that was never registered.
    /// </summary>
    void Unregister(CoreWebView2 coreWebView);

    /// <summary>
    /// Gives the surface keyboard focus (native first responder on macOS, managed focus on Windows), applies its
    /// optional DOM-side focus, and reports the focus. Used by tab clicks, document opens, the console title
    /// bar, the find bar, and layout-mode changes. Returns false when the surface is not registered yet, so a
    /// caller granting focus to a freshly created surface can retry.
    /// </summary>
    bool GrantFocus(WebView2 webView);

    /// <summary>
    /// Re-establishes the focused surface's keyboard focus after platform housekeeping has displaced it
    /// without a real focus change. On macOS a programmatic managed-focus re-assert (an Explorer item
    /// container recycling during a background refresh) makes Uno resign the native first responder, which
    /// deactivates the focused page: the caret hides and keystrokes beep. A no-op when no hosted surface
    /// holds focus.
    /// </summary>
    void ReassertNativeFocus();

    /// <summary>
    /// Delivers a native key event to the focused surface's web view, bypassing the managed pipeline.
    /// Used for editing-command keys (Backspace, Enter, the arrows) that the platform routes into its own
    /// command handling instead of the first responder while a hosted surface holds focus. Returns false
    /// when no hosted surface holds focus. macOS-only, like TryHandleTabKey.
    /// </summary>
    bool TryForwardKeyEvent(IntPtr nativeKeyEvent);

    /// <summary>
    /// Handles a Tab or Shift+Tab press while a hosted surface holds focus. The focused surface's edit target
    /// acts on it first (a code editor indents, the spreadsheet moves the active cell); a surface without one
    /// has the key delivered to its native web view so the page applies its own Tab behaviour, such as moving
    /// between form fields. Returns false when no hosted surface holds focus, letting normal focus navigation
    /// proceed. Called by the macOS native key monitor with the NSEvent handle; the other heads deliver Tab
    /// through their managed pipeline and never call this.
    /// </summary>
    bool TryHandleTabKey(bool shift, IntPtr nativeKeyEvent);
}
