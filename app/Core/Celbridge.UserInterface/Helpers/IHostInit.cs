using StreamJsonRpc;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// RPC service interface for host initialization.
/// </summary>
public interface IHostInit
{
    /// <summary>
    /// Initializes the host connection with the WebView.
    /// Returns the document content, metadata, and localization strings.
    /// </summary>
    [JsonRpcMethod(HostRpcMethods.Initialize)]
    Task<InitializeResult> InitializeAsync(InitializeParams request);
}
