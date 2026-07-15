using StreamJsonRpc;

namespace Celbridge.Host;

public static class ContextRpcMethods
{
    public const string GetContext = "host/getContext";
}

/// <summary>
/// RPC service interface for retrieving the custom editor's capability context.
/// </summary>
public interface IHostContext
{
    /// <summary>
    /// Returns the editor's resolved tool allowlist, secrets, and options.
    /// </summary>
    [JsonRpcMethod(ContextRpcMethods.GetContext)]
    CelbridgeContext GetContext();
}
