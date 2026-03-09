using StreamJsonRpc;

namespace Celbridge.Host;

public static class CodePreviewRpcMethods
{
    // Host-to-client notifications
    public const string SetContext = "codePreview/setContext";
    public const string Update = "codePreview/update";
    public const string Scroll = "codePreview/scroll";

    // Client-to-host notifications
    public const string OpenResource = "codePreview/openResource";
    public const string OpenExternal = "codePreview/openExternal";
    public const string SyncToEditor = "codePreview/syncToEditor";
}

/// <summary>
/// RPC service interface for handling notifications from code preview JavaScript (no response expected).
/// Implement this interface to handle code preview-related messages from the WebView.
/// </summary>
public interface IHostCodePreview
{
    /// <summary>
    /// Called when the code preview requests to open a local resource (e.g., a linked document).
    /// </summary>
    [JsonRpcMethod(CodePreviewRpcMethods.OpenResource)]
    void OnOpenResource(string href);

    /// <summary>
    /// Called when the code preview requests to open an external URL in the browser.
    /// </summary>
    [JsonRpcMethod(CodePreviewRpcMethods.OpenExternal)]
    void OnOpenExternal(string href);

    /// <summary>
    /// Called when the code preview requests to sync the editor scroll position.
    /// </summary>
    [JsonRpcMethod(CodePreviewRpcMethods.SyncToEditor)]
    void OnSyncToEditor(double scrollPercentage);
}

public static class HostCodePreviewExtensions
{
    /// <summary>
    /// Sets the document context for the code preview pane (base path for resolving relative resources).
    /// </summary>
    public static Task NotifyCodePreviewSetContextAsync(this CelbridgeHost host, string basePath)
        => host.Rpc.NotifyWithParameterObjectAsync(CodePreviewRpcMethods.SetContext, new { basePath });

    /// <summary>
    /// Updates the code preview content.
    /// </summary>
    public static Task NotifyCodePreviewUpdateAsync(this CelbridgeHost host, string content)
        => host.Rpc.NotifyWithParameterObjectAsync(CodePreviewRpcMethods.Update, new { content });

    /// <summary>
    /// Scrolls the code preview to a specific position (0.0 to 1.0).
    /// </summary>
    public static Task NotifyCodePreviewScrollAsync(this CelbridgeHost host, double scrollPercentage)
        => host.Rpc.NotifyWithParameterObjectAsync(CodePreviewRpcMethods.Scroll, new { scrollPercentage });
}
