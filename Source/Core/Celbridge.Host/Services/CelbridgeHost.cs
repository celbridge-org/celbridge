using StreamJsonRpc;

namespace Celbridge.Host;

/// <summary>
/// Host-side JSON-RPC communication facade for WebView2.
/// This is the C# counterpart to CelbridgeClient in JavaScript.
/// Owns the RPC infrastructure and provides a clean API for document views.
/// </summary>
public class CelbridgeHost : IDisposable
{
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
