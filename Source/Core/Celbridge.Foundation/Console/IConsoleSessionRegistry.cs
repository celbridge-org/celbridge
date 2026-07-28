namespace Celbridge.Console;

/// <summary>
/// The lifecycle state of an open console. A None-binding console (a plain shell) is Ready as soon as its
/// pty starts; a host-bound console (CelProxy or Mcp) stays Connecting until its client says hello.
/// </summary>
public enum ConsoleSessionState
{
    Launching,
    Connecting,
    Ready,
    Disconnected,
}

/// <summary>
/// The details a console view supplies to register itself as an open console: the resource that owns it,
/// its session type, its tab title, how it reaches the host, its effective runners, and the injector that
/// writes text into its pty.
/// </summary>
public sealed record ConsoleRegistration(
    ResourceKey ResourceKey,
    string TypeId,
    string Title,
    ConsoleHostBinding HostBinding,
    IReadOnlyList<ConsoleRunner> Runners,
    IConsoleCommandInjector Injector);

/// <summary>
/// A registered open console: the host-assigned session id (which doubles as the handshake token), the
/// .console resource that owns it, its session type, its tab title, its current state, and the transport
/// connection it bound to (null until a host-bound client says hello).
/// </summary>
public sealed record ConsoleSession(
    Guid SessionId,
    ResourceKey ResourceKey,
    string TypeId,
    string Title,
    ConsoleSessionState State,
    int? ConnectionId);

/// <summary>
/// A console that can run a clicked file: the console to run it in, the resource for disambiguation, a
/// display name for the menu, and the command template whose "{script_path}" is replaced with the path.
/// </summary>
public sealed record ConsoleRunTarget(
    Guid SessionId,
    ResourceKey ResourceKey,
    string DisplayName,
    string CommandTemplate);

/// <summary>
/// Writes text into a console's pty. Implemented by a console view so the host can inject a run command or
/// shortcut into the session it targets.
/// </summary>
public interface IConsoleCommandInjector
{
    /// <summary>
    /// Injects a line of text into the console, clearing any partial input first and submitting it.
    /// </summary>
    void InjectCommand(string text);
}

/// <summary>
/// Tracks the open consoles in a workspace and, for host-bound consoles, maps inbound transport connections
/// to the console that launched them via the session/handshake handshake. Owns the shared cel-proxy JSON-RPC
/// listener so peer consoles multiplex over one port. Also resolves the Explorer Run menu's targets.
/// </summary>
public interface IConsoleSessionRegistry
{
    /// <summary>
    /// Ensures the shared cel-proxy JSON-RPC listener is running and returns its loopback port. Host-bound
    /// consoles inject this as CELBRIDGE_RPC_PORT so every peer dials the one shared listener.
    /// </summary>
    Task<int> EnsureRpcListenerAsync();

    /// <summary>
    /// Registers, or on reopen replaces, the open console for a resource, returning it with a fresh session
    /// id that doubles as the handshake token. A None-binding console starts Ready; a host-bound one starts
    /// Connecting.
    /// </summary>
    ConsoleSession Register(ConsoleRegistration registration);

    /// <summary>
    /// Moves the console for a resource to a new state, broadcasting ConsoleSessionStateChangedMessage.
    /// </summary>
    void SetState(ResourceKey resourceKey, ConsoleSessionState state);

    /// <summary>
    /// Removes the console for a resource (its tab closed or its process was disposed).
    /// </summary>
    void Unregister(ResourceKey resourceKey);

    /// <summary>
    /// Binds a transport connection to the console whose token matches, moving it to Ready. Returns false
    /// if no open console matches the token.
    /// </summary>
    bool TryBindConnection(Guid sessionToken, int connectionId, out ConsoleSession? session);

    /// <summary>
    /// Handles a lost transport connection, moving its bound console to Disconnected.
    /// </summary>
    void OnConnectionLost(int connectionId);

    /// <summary>
    /// Looks up the open console for a resource.
    /// </summary>
    bool TryGetByResource(ResourceKey resourceKey, out ConsoleSession? session);

    /// <summary>
    /// Returns the Ready consoles whose effective runners cover a file extension, as Run menu targets.
    /// </summary>
    IReadOnlyList<ConsoleRunTarget> GetRunTargets(string fileExtension);

    /// <summary>
    /// Runs a script in a specific console by substituting its path into the matching runner template,
    /// appending any arguments, and injecting the result into that console's pty.
    /// </summary>
    void RunScript(Guid sessionId, string scriptPath, string arguments);
}
