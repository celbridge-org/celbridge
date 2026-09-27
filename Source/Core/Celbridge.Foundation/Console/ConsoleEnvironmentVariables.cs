namespace Celbridge.Console;

/// <summary>
/// Environment variable names seeded into every console session: the terminal's identity, and the
/// connection details any child process (a typed celbridge-py, a spawned terminal) uses to dial back into
/// the workspace and attribute itself.
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
    /// The terminal's name, which programs read to choose their handling for the terminal they run in.
    /// </summary>
    public const string TerminalProgram = "TERM_PROGRAM";

    /// <summary>
    /// The terminal's version, read alongside its name.
    /// </summary>
    public const string TerminalProgramVersion = "TERM_PROGRAM_VERSION";
}
