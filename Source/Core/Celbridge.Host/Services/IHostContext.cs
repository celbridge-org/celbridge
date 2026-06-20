using StreamJsonRpc;

namespace Celbridge.Host;

public static class ContextRpcMethods
{
    public const string GetContext = "host/getContext";
}

/// <summary>
/// RPC service interface for retrieving the contribution editor's capability context.
/// The Skia head cannot inject window.__celbridgeContext before navigation, so the JS
/// client fetches the context over the bridge during startup instead.
/// </summary>
public interface IHostContext
{
    /// <summary>
    /// Returns the editor's resolved tool allowlist, secrets, and options.
    /// </summary>
    [JsonRpcMethod(ContextRpcMethods.GetContext)]
    CelbridgeContext GetContext();
}
