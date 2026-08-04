namespace Celbridge.Console;

/// <summary>
/// The run state of a console session. Starting until its pty launches, Running while the pty lives, Ended
/// once the process exits, Failed if the launch never succeeded. A reopen starts a fresh session.
/// </summary>
public enum ConsoleSessionRunState
{
    Starting,
    Running,
    Ended,
    Failed,
}

/// <summary>
/// What an attaching console view needs to render a live session: its state, the failure reason when it
/// failed, whether the startup phase is still pending (keep the starting veil up), the buffered output to
/// replay, and the raw .console text the session launched from (for the settings form's divergence check).
/// </summary>
public sealed record ConsoleAttachSnapshot(
    ConsoleSessionRunState State,
    string? Error,
    bool StartupPending,
    string Replay,
    string? LaunchedConfigToml);

/// <summary>
/// A console that can run a clicked file, addressed by its session id. The display name is its file name,
/// carrying as much of the parent path as it takes to tell it apart from the other targets.
/// </summary>
public sealed record ConsoleRunTarget(
    Guid SessionId,
    ResourceKey ResourceKey,
    string DisplayName);

/// <summary>
/// A console view, as the live session it is attached to sees it.
/// </summary>
public interface IConsoleView
{
    /// <summary>
    /// Delivers live terminal output.
    /// </summary>
    void OnOutput(string text);

    /// <summary>
    /// Reports that the session's process has exited.
    /// </summary>
    void OnSessionEnded();

    /// <summary>
    /// Reports that the startup phase is over and the terminal can be revealed.
    /// </summary>
    void OnStartupComplete();
}

/// <summary>
/// Owns the workspace's console sessions. A session's lifetime follows its .console document rather than
/// any view, so a view is an attachment to a session that is already running.
/// </summary>
public interface IConsoleSessionService
{
    /// <summary>
    /// Returns true while any session in the workspace has a live process, so callers can defer work that
    /// would disturb the files a running session is executing from. A session that has not yet launched
    /// its pty does not count as running.
    /// </summary>
    bool HasRunningSessions { get; }

    /// <summary>
    /// Starts the session for a .console document if it is not already running. Failures are recorded on
    /// the session and surface when a view attaches.
    /// </summary>
    Task EnsureStartedAsync(ResourceKey resource);

    /// <summary>
    /// Attaches a view to the session, replacing any previous attachment, and returns the snapshot to
    /// render. Starts the session first if the document-open path has not already done so. The pty is
    /// resized to the view's terminal size, which is the first accurate size a headless session has had.
    /// </summary>
    Task<ConsoleAttachSnapshot> AttachAsync(ResourceKey resource, IConsoleView attachedView, int cols, int rows);

    /// <summary>
    /// Detaches a view. The session keeps running and buffering. A view that is not the current
    /// attachment is ignored.
    /// </summary>
    void Detach(ResourceKey resource, IConsoleView attachedView);

    /// <summary>
    /// Relaunches the session from the .console file on disk, keeping the attachment, and returns the
    /// fresh snapshot.
    /// </summary>
    Task<ConsoleAttachSnapshot> ReopenAsync(ResourceKey resource, int cols, int rows);

    /// <summary>
    /// Resizes the session's pty.
    /// </summary>
    void Resize(ResourceKey resource, int cols, int rows);

    /// <summary>
    /// Writes user input to the session's pty. Dropped while the startup phase is pending.
    /// </summary>
    void Input(ResourceKey resource, string data);

    /// <summary>
    /// Ends the session and releases its process. A resource with no session is ignored.
    /// </summary>
    void EndSession(ResourceKey resource);

    /// <summary>
    /// Binds a transport connection to the session whose handshake token matches, broadcasting
    /// ConsoleSessionConnectedMessage. Returns false if no session matches the token.
    /// </summary>
    bool TryBindConnection(Guid sessionToken, int connectionId);

    /// <summary>
    /// Clears a lost transport connection's binding. Session liveness follows the pty, so the session
    /// itself is unaffected, but its runners become stale until a client reconnects.
    /// </summary>
    void OnConnectionLost(int connectionId);

    /// <summary>
    /// Returns the built-in runners each registered session type contributes, keyed by type id. A console's
    /// own runners layer over these, so the settings form can show what its type already handles.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<ConsoleRunner>> GetBuiltInRunners();

    /// <summary>
    /// Returns the sessions whose effective runners cover a file extension, as Run menu targets sorted by
    /// display name. A session whose client connection was bound and then lost (its REPL exited back to
    /// the shell prompt) is excluded until a client reconnects or the console reopens.
    /// </summary>
    IReadOnlyList<ConsoleRunTarget> GetRunTargets(string fileExtension);

    /// <summary>
    /// Returns the invocation that runs a file in a session, built from the first runner whose extensions
    /// cover the file type, with any arguments appended. Fails if the session has ended, has lost its
    /// client connection, or carries no runner for that file type.
    /// </summary>
    Result<string> ResolveRunnerInvocation(Guid sessionId, string scriptPath, string arguments);

    /// <summary>
    /// Submits an invocation to a session, as if it were typed at the prompt: any partially typed input is
    /// cleared first, and the line queues behind the startup lines while the session is still starting.
    /// Fails if the session has ended or has lost its client connection.
    /// </summary>
    Result SubmitInvocation(Guid sessionId, string invocation);

    /// <summary>
    /// Submits an invocation to whichever session a console document is currently running, rather than to
    /// one specific launch. A resource with no session is ignored.
    /// </summary>
    void SubmitInvocation(ResourceKey resource, string invocation);
}
