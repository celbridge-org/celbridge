using StreamJsonRpc;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// JSON-RPC method names for the language channel. Mirrored by language-client.js in the code editor.
/// </summary>
public static class LanguageChannelRpcMethods
{
    // Client to host
    public const string Open = "language/open";
    public const string Send = "language/send";
    public const string Close = "language/close";

    // Host to client
    public const string Message = "language/message";
    public const string Closed = "language/closed";
}

/// <summary>
/// Inbound RPC calls from a code editor page on the language channel.
/// </summary>
public interface ILanguageChannelRpc
{
    /// <summary>
    /// Opens a connection to the language server of the page's document, in place of any connection already open.
    /// </summary>
    [JsonRpcMethod(LanguageChannelRpcMethods.Open)]
    void OnOpen();

    /// <summary>
    /// Passes one message from the page to the open connection. The message is JSON text in the Language Server
    /// Protocol's shape.
    /// </summary>
    [JsonRpcMethod(LanguageChannelRpcMethods.Send)]
    void OnSend(string message);

    /// <summary>
    /// Closes the open connection.
    /// </summary>
    [JsonRpcMethod(LanguageChannelRpcMethods.Close)]
    void OnClose();
}
