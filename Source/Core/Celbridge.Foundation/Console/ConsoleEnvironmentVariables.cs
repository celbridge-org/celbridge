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

    /// <summary>
    /// The folders the console start-up moves to the front of PATH after the user's profile runs. They are
    /// joined with the platform's path separator, in the order they should appear.
    /// </summary>
    public const string PathFolders = "CELBRIDGE_CONSOLE_PATH_FOLDERS";

    /// <summary>
    /// Set when the python feature is on. The console start-up reads it after the user's profile runs. When it is
    /// set, the start-up removes the uv and Python variables, and any alias or function named uv, uvx or
    /// celbridge-py.
    /// </summary>
    public const string PythonFeature = "CELBRIDGE_CONSOLE_PYTHON_FEATURE";
}
