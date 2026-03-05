using StreamJsonRpc;

namespace Celbridge.Console.Services;

/// <summary>
/// RPC interface for handling notifications from the console JavaScript client.
/// Implement this interface in ConsolePanel to receive console events.
/// </summary>
[JsonRpcContract]
public interface IConsoleNotifications
{
    /// <summary>
    /// Called when the user types input in the console.
    /// </summary>
    [JsonRpcMethod(ConsoleRpcMethods.Input)]
    void OnConsoleInput(string data);

    /// <summary>
    /// Called when the console is resized.
    /// </summary>
    [JsonRpcMethod(ConsoleRpcMethods.Resize)]
    void OnConsoleResize(int cols, int rows);
}
