using StreamJsonRpc;

namespace Celbridge.Host;

/// <summary>
/// JSON-RPC method names used for WebView2 communication between C# host and JavaScript client.
/// </summary>
public static class HostRpcMethods
{
    // Host initialization
    public const string Initialize = "host/initialize";

    // Keyboard shortcuts
    public const string KeyboardShortcut = "host/keyboardShortcut";

    // Document operations
    public const string DocumentLoad = "document/load";
    public const string DocumentSave = "document/save";

    // Document notifications
    public const string DocumentChanged = "document/changed";
    public const string DocumentRequestSave = "document/requestSave";
    public const string DocumentExternalChange = "document/externalChange";

    // Dialog operations
    public const string DialogPickImage = "dialog/pickImage";
    public const string DialogPickFile = "dialog/pickFile";
    public const string DialogAlert = "dialog/alert";

    // Link operations
    public const string LinkClicked = "link/clicked";

    // Import operations
    public const string ImportComplete = "import/complete";

    // Lifecycle notifications
    public const string ClientReady = "client/ready";

    // Localization
    public const string LocalizationUpdated = "localization/updated";

    // Editor scroll notifications
    public const string EditorScrollChanged = "editor/scrollChanged";
}

/// <summary>
/// Host-side JSON-RPC communication facade for WebView2.
/// This is the C# counterpart to CelbridgeClient in JavaScript.
/// Owns the RPC infrastructure and provides a clean API for document views.
/// </summary>
public class CelbridgeHost : IDisposable
{
    private readonly IHostChannel _channel;
    private readonly RpcMessageHandler _rpcHandler;
    private bool _disposed;

    /// <summary>
    /// Gets the underlying JsonRpc instance for advanced scenarios (e.g., module-specific RPC methods).
    /// Prefer using the typed methods on CelbridgeHost for standard operations.
    /// </summary>
    public JsonRpc Rpc { get; }

    /// <summary>
    /// Creates a new CelbridgeHost with the specified channel.
    /// </summary>
    public CelbridgeHost(IHostChannel channel)
    {
        _channel = channel;
        _rpcHandler = new RpcMessageHandler(channel);
        Rpc = new JsonRpc(_rpcHandler);

        // Ensure RPC method handlers run on the UI thread
        Rpc.SynchronizationContext = SynchronizationContext.Current;
    }

    /// <summary>
    /// Registers a target object that implements RPC methods.
    /// </summary>
    public void AddLocalRpcTarget<T>(T target) where T : class
    {
        Rpc.AddLocalRpcTarget<T>(target, null);
    }

    /// <summary>
    /// Starts listening for incoming RPC messages.
    /// Call this after registering all RPC targets.
    /// </summary>
    public void StartListening()
    {
        Rpc.StartListening();
    }

    /// <summary>
    /// Requests the WebView to save the current document content.
    /// JS should respond by calling document/save.
    /// </summary>
    public Task NotifyRequestSaveAsync()
    {
        return Rpc.NotifyAsync(HostRpcMethods.DocumentRequestSave);
    }

    /// <summary>
    /// Notifies the WebView that the document has been externally modified.
    /// </summary>
    public Task NotifyExternalChangeAsync()
    {
        return Rpc.NotifyAsync(HostRpcMethods.DocumentExternalChange);
    }

    /// <summary>
    /// Notifies the WebView that localization strings have been updated.
    /// </summary>
    public Task NotifyLocalizationUpdatedAsync(Dictionary<string, string> strings)
    {
        return Rpc.NotifyWithParameterObjectAsync(HostRpcMethods.LocalizationUpdated, new { strings });
    }

    /// <summary>
    /// Disposes the host and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Rpc.Dispose();
        _rpcHandler.Dispose();
    }
}
