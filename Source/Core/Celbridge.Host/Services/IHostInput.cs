using StreamJsonRpc;

namespace Celbridge.Host;

public static class InputRpcMethods
{
    public const string KeyboardShortcut = "input/keyboardShortcut";
    public const string LinkClicked = "input/linkClicked";
    public const string EditorScrollChanged = "input/scrollChanged";
    public const string PreviewScrollChanged = "input/previewScrollChanged";
    public const string OpenResource = "input/openResource";
    public const string OpenExternal = "input/openExternal";

    // Host to client. Asks the WebView to release its DOM focus.
    public const string ReleaseFocus = "input/releaseFocus";

    // Host to client. Asks the editor to run one of its own edit commands (selectAll, undo, redo).
    public const string PerformEdit = "input/performEdit";

    // Client to host. Reports which edit verbs the editor can currently perform.
    public const string EditAvailabilityChanged = "input/editAvailabilityChanged";

    // Client to host. Reports that the WebView content has received focus. This is the focus signal on the
    // Skia heads, where the WinUI WebView.GotFocus event does not fire for clicks inside the WebView.
    public const string FocusReceived = "input/focusReceived";
}

/// <summary>
/// RPC service interface for handling user input notifications from JavaScript (no response expected).
/// </summary>
public interface IHostInput
{
    /// <summary>
    /// Called when a keyboard shortcut is pressed in the WebView.
    /// </summary>
    [JsonRpcMethod(InputRpcMethods.KeyboardShortcut)]
    void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey);

    /// <summary>
    /// Called when a link is clicked in the WebView.
    /// </summary>
    [JsonRpcMethod(InputRpcMethods.LinkClicked)]
    void OnLinkClicked(string href) { }

    /// <summary>
    /// Called when the scroll position changes in the editor.
    /// </summary>
    [JsonRpcMethod(InputRpcMethods.EditorScrollChanged)]
    void OnScrollPositionChanged(double scrollPercentage) { }

    /// <summary>
    /// Called when the scroll position changes in a preview pane.
    /// </summary>
    [JsonRpcMethod(InputRpcMethods.PreviewScrollChanged)]
    void OnPreviewScrollChanged(double scrollPercentage) { }

    /// <summary>
    /// Called when a local resource link is clicked in the WebView.
    /// Host resolves the href relative to the current document folder and opens the resource.
    /// </summary>
    [JsonRpcMethod(InputRpcMethods.OpenResource)]
    void OnOpenResource(string href) { }

    /// <summary>
    /// Called when an external URL is clicked in the WebView.
    /// Host opens the URL in the default browser.
    /// </summary>
    [JsonRpcMethod(InputRpcMethods.OpenExternal)]
    void OnOpenExternal(string href) { }

    /// <summary>
    /// Called when a WebView editor reports which edit verbs it can currently perform, so the host can
    /// drive menu enable state.
    /// </summary>
    [JsonRpcMethod(InputRpcMethods.EditAvailabilityChanged)]
    void OnEditAvailabilityChanged(
        bool canCopy,
        bool canCut,
        bool canPaste,
        bool canSelectAll,
        bool canUndo,
        bool canRedo)
    { }

    /// <summary>
    /// Called when the WebView content gains focus. The host marshals to the UI thread and reports the
    /// surface to the focus service. This is the focus signal on the Skia heads (where WebView.GotFocus
    /// does not fire).
    /// </summary>
    [JsonRpcMethod(InputRpcMethods.FocusReceived)]
    void OnFocusReceived() { }
}

public static class HostInputExtensions
{
    /// <summary>
    /// Asks the WebView to release its DOM focus when focus moves to another panel, so the editor
    /// caret stops on heads where WebView and host focus are not integrated. The client handles this
    /// generically by releasing focus from document.activeElement.
    /// </summary>
    public static Task NotifyReleaseFocusAsync(this CelbridgeHost host)
        => host.Rpc.NotifyAsync(InputRpcMethods.ReleaseFocus);

    /// <summary>
    /// Asks the editor to run one of its own edit commands. The command is the editor command name
    /// (selectAll, undo, redo). Copy, cut, and paste are host-mediated and not sent here.
    /// </summary>
    public static Task NotifyPerformEditAsync(this CelbridgeHost host, string command)
        => host.Rpc.NotifyWithParameterObjectAsync(InputRpcMethods.PerformEdit, new { command });
}
