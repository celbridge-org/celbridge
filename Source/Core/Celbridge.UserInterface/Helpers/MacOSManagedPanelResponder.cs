#if !WINDOWS
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Keeps the AppKit first responder aligned with managed-panel focus on the Skia heads. A hosted WebView
/// stays the window's first responder even after a managed Uno panel (Explorer, Inspector, Search) gains
/// focus, so the native Edit-menu shortcuts (cut:/copy:/paste:/undo:/redo:) would route to that stale
/// WebView instead of the managed panel. When one of those panels gains focus, this makes the window
/// content view the first responder, so the shortcuts disable for the panel and the key equivalents fall
/// through to Uno's own keyboard handling. macOS-only.
/// </summary>
internal static class MacOSManagedPanelResponder
{
    // A stable recipient kept alive for the process lifetime so the subscription survives.
    private static readonly object Recipient = new();
    private static bool _started;

    public static void Start(IMessengerService messengerService)
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
        messengerService.Register<PanelFocusChangedMessage>(Recipient, OnPanelFocusChanged);
    }

    private static void OnPanelFocusChanged(object recipient, PanelFocusChangedMessage message)
    {
        // Documents and Console host WebViews that should keep first-responder status; the other panels
        // are managed Uno controls that are not AppKit responders.
        switch (message.FocusedPanel)
        {
            case WorkspacePanel.Explorer:
            case WorkspacePanel.Inspector:
            case WorkspacePanel.Search:
                MacOSWindowInterop.MakeContentViewFirstResponder();
                break;
        }
    }
}
#endif
