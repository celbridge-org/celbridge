namespace Celbridge.Console;

/// <summary>
/// Environment variable names seeded into every console session so any child process (a typed
/// celbridge-py, a spawned terminal) can dial back into the workspace and attribute itself.
/// </summary>
public static class ConsoleEnvironmentVariables
{
    /// <summary>
    /// The loopback port of the shared cel-proxy JSON-RPC listener.
    /// </summary>
    public const string RpcPort = "CELBRIDGE_RPC_PORT";

    /// <summary>
    /// The launching console's session token, echoed back via session/handshake to attribute a
    /// connection to its console.
    /// </summary>
    public const string SessionToken = "CELBRIDGE_SESSION_TOKEN";
}
