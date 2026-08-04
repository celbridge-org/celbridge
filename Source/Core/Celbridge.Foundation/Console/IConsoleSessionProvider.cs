namespace Celbridge.Console;

/// <summary>
/// The command a session type injects into its console's shell once the shell is up: an executable (or
/// command name resolved on the shell's PATH), its arguments, and optional environment variables seeded
/// into the session so a manual re-run of the command reproduces the same launch. An empty executable
/// means the session is just the plain shell with nothing injected. Environment entries are merged
/// add-if-absent, so a value the console's own [session.environment] sets wins. HandlesStartupScript says
/// the provider has arranged to run the console's startup script itself, so the host must not also type it
/// into the pty: a runtime that discards pending input as it takes over the terminal (an interactive
/// interpreter, typically) has to receive the script through its own startup mechanism instead.
/// </summary>
public sealed record ConsoleStartupInvocation(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null,
    bool HandlesStartupScript = false)
{
    /// <summary>
    /// The startup command that injects nothing, leaving the session at the shell prompt.
    /// </summary>
    public static ConsoleStartupInvocation None { get; } = new(string.Empty, Array.Empty<string>());
}

/// <summary>
/// A way of running a file in a console: the file extensions it handles and a command template injected to
/// run a matching file, where "{resource}" is replaced with the file's path. Used both for the runners a
/// session type provides and for the ones a .console file declares, which are the same thing from different
/// sources, so the property names match the file's own keys. Only a built-in runner carries a BuiltInId,
/// which is how a console names the one it switches off; runners a .console file declares are anonymous
/// entries in its own list and leave it empty. The id is unique across every session type, so it is
/// conventionally the type id, qualified further with a hyphen ("python-notebook") only by a type
/// contributing more than one runner.
/// </summary>
public sealed record ConsoleRunner(
    IReadOnlyList<string> Extensions,
    string Command,
    string BuiltInId = "");

/// <summary>
/// The resolved configuration a provider builds a startup command from. WorkingDirectory is as written in
/// the config and resolves against ProjectFolderPath, and the environment variables already carry the RPC
/// port and session token. Fields a given type does not use are left at their defaults.
/// </summary>
public sealed record ConsoleSessionContext(
    ResourceKey ResourceKey,
    string TypeId,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    string ProjectFolderPath,
    IReadOnlyList<string>? Dependencies = null,
    string? RuntimeVersion = null,
    string? StartupScript = null);

/// <summary>
/// Builds the startup command for one console session type, keyed by TypeId (e.g. "shell"). Every console
/// session runs the platform shell in the shared console environment; a session type only decides what
/// command, if any, is injected into that shell once it is up.
/// </summary>
public interface IConsoleSessionProvider
{
    /// <summary>
    /// The session-type key this provider handles, matched against a .console file's [session] type.
    /// </summary>
    string TypeId { get; }

    /// <summary>
    /// The built-in runners this type contributes (file extensions plus a run-command template), or an
    /// empty list if the type runs nothing by default.
    /// </summary>
    IReadOnlyList<ConsoleRunner> BuiltInRunners { get; }

    /// <summary>
    /// Builds the startup command for a session from its resolved config, or a failure if the config
    /// cannot produce a runnable command. May perform launch prerequisites (e.g. installing a toolchain).
    /// </summary>
    Task<Result<ConsoleStartupInvocation>> BuildStartupInvocationAsync(ConsoleSessionContext context);
}
