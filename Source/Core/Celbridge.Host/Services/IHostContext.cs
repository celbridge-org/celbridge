using StreamJsonRpc;

namespace Celbridge.Host;

public static class ContextRpcMethods
{
    public const string GetContext = "host/getContext";
}

/// <summary>
/// RPC service interface for retrieving the contribution editor's capability context. The JS client fetches
/// it over the bridge during startup, on every head.
/// </summary>
public interface IHostContext
{
    /// <summary>
    /// Returns the editor's resolved tool allowlist, secrets, and options.
    /// </summary>
    [JsonRpcMethod(ContextRpcMethods.GetContext)]
    CelbridgeContext GetContext();
}
