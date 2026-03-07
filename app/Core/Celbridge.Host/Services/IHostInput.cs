using StreamJsonRpc;

namespace Celbridge.Host;

/// <summary>
/// RPC service interface for handling user input notifications from JavaScript (no response expected).
/// </summary>
public interface IHostInput
{
    /// <summary>
    /// Called when a keyboard shortcut is pressed in the WebView.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.KeyboardShortcut)]
    void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey);

    /// <summary>
    /// Called when a link is clicked in the WebView.
    /// Override to handle link clicks.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.LinkClicked)]
    void OnLinkClicked(string href) { }

    /// <summary>
    /// Called when the scroll position changes in the editor.
    /// Override to handle scroll position changes.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.EditorScrollChanged)]
    void OnScrollPositionChanged(double scrollPercentage) { }
}
