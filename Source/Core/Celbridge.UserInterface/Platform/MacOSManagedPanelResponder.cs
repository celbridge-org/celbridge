using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Reconciles focus on every managed panel-focus change on the macOS Skia head. A hosted WebView stays
/// the window's first responder even after a managed Uno panel (Explorer, Search) gains
/// focus, so the native Edit-menu shortcuts (cut:/copy:/paste:/undo:/redo:) would route to that stale
/// WebView instead of the managed panel. Reconciling returns native focus to the window content when no
/// web surface holds focus, so the shortcuts disable for the panel and the key equivalents fall through
/// to Uno's own keyboard handling. macOS-only.
/// </summary>
internal static class MacOSManagedPanelResponder
{
    // A stable recipient kept alive for the process lifetime so the subscription survives.
    private static readonly object Recipient = new();
    private static IFocusReconciler? _focusReconciler;
    private static bool _started;

    public static void Start(IMessengerService messengerService, IFocusReconciler focusReconciler)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (_started)
        {
            return;
        }

        _started = true;
        _focusReconciler = focusReconciler;
        messengerService.Register<PanelFocusChangedMessage>(Recipient, OnPanelFocusChanged);
    }

    private static void OnPanelFocusChanged(object recipient, PanelFocusChangedMessage message)
    {
        // The focus service releases the focused web surface before sending the message, so the
        // reconciler observes the post-release state: a managed panel taking focus derives to the
        // content view becoming first responder, and a web surface taking focus derives to that
        // surface keeping it.
        _focusReconciler?.Reconcile();
    }
}
