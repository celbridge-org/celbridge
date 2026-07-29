namespace Celbridge.Console;

/// <summary>
/// The lifecycle state of an open console: Ready while its pty runs, Ended once the process exits. A
/// reopen registers a fresh session.
/// </summary>
public enum ConsoleSessionState
{
    Ready,
    Ended,
}

/// <summary>
/// The details a console view supplies to register itself as an open console.
/// </summary>
public sealed record ConsoleRegistration(
    ResourceKey ResourceKey,
    string TypeId,
    string Title,
    IReadOnlyList<ConsoleRunner> Runners,
    IConsoleCommandInjector Injector);

/// <summary>
/// A registered open console. Its session id doubles as the handshake token, and its connection id is null
/// until a client says hello.
/// </summary>
public sealed record ConsoleSession(
    Guid SessionId,
    ResourceKey ResourceKey,
    string TypeId,
    string Title,
    ConsoleSessionState State,
    int? ConnectionId);

/// <summary>
/// A console that can run a clicked file, addressed by its session id.
/// </summary>
public sealed record ConsoleRunTarget(
    Guid SessionId,
    ResourceKey ResourceKey,
    string DisplayName);

/// <summary>
/// Writes text into a console's pty.
/// </summary>
public interface IConsoleCommandInjector
{
    /// <summary>
    /// Injects a line of text into the console, clearing any partial input first and submitting it.
    /// </summary>
    void InjectCommand(string text);
}

/// <summary>
/// Tracks the open consoles in a workspace and maps inbound transport connections to the console that
/// launched them. Session state follows the pty (the connection is attribution only). Owns the shared
/// cel-proxy JSON-RPC listener, and resolves the Explorer Run menu's targets.
/// </summary>
public interface IConsoleSessionRegistry
{
    /// <summary>
    /// Ensures the shared cel-proxy JSON-RPC listener is running and returns its loopback port.
    /// </summary>
    Task<int> EnsureRpcListenerAsync();

    /// <summary>
    /// Registers, or on reopen replaces, the open console for a resource as Ready, returning it with a
    /// fresh session id that doubles as the handshake token.
    /// </summary>
    ConsoleSession Register(ConsoleRegistration registration);

    /// <summary>
    /// Moves the console for a resource to a new state, broadcasting ConsoleSessionStateChangedMessage.
    /// </summary>
    void SetState(ResourceKey resourceKey, ConsoleSessionState state);

    /// <summary>
    /// Removes the console for a resource.
    /// </summary>
    void Unregister(ResourceKey resourceKey);

    /// <summary>
    /// Binds a transport connection to the console whose token matches, broadcasting
    /// ConsoleSessionConnectedMessage. Returns false if no open console matches the token.
    /// </summary>
    bool TryBindConnection(Guid sessionToken, int connectionId, out ConsoleSession? session);

    /// <summary>
    /// Clears a lost transport connection's binding. The console's state is unaffected, since session
    /// liveness follows the pty.
    /// </summary>
    void OnConnectionLost(int connectionId);

    /// <summary>
    /// Looks up the open console for a resource.
    /// </summary>
    bool TryGetByResource(ResourceKey resourceKey, out ConsoleSession? session);

    /// <summary>
    /// Returns the Ready consoles whose effective runners cover a file extension, as Run menu targets in
    /// open order. A console whose client connection was bound and then lost (its REPL exited back to the
    /// shell prompt) is excluded until a client reconnects or the console reopens.
    /// </summary>
    IReadOnlyList<ConsoleRunTarget> GetRunTargets(string fileExtension);

    /// <summary>
    /// Runs a script in a specific console by substituting its path into the matching runner template,
    /// appending any arguments, and injecting the result into that console's pty.
    /// </summary>
    void RunScript(Guid sessionId, string scriptPath, string arguments);
}
