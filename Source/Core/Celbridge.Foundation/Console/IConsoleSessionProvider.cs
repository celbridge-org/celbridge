namespace Celbridge.Console;

/// <summary>
/// How a console session reaches the Celbridge host. None is a plain pty with no host RPC; CelProxy dials
/// the shared JSON-RPC server for a cel.* proxy; Mcp is handed an MCP port for an MCP-speaking client.
/// </summary>
public enum ConsoleHostBinding
{
    None,
    CelProxy,
    Mcp,
}

/// <summary>
/// The resolved inputs to start a console session's process: the command line to run, the working folder
/// to run it in, and the environment variables to inject. The pty backend merges the environment with the
/// process environment, so this carries only the deltas.
/// </summary>
public sealed record ConsoleLaunchSpec(
    string CommandLine,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// A default way a session type runs a file: the file extensions it handles and a command template
/// injected to run a matching file, where "{script_path}" is replaced with the file path.
/// </summary>
public sealed record ConsoleRunner(
    IReadOnlyList<string> FileExtensions,
    string CommandTemplate);

/// <summary>
/// The resolved configuration a provider builds a launch spec from: the console resource, its session
/// type, the executable and arguments, the working folder as written in the config, the environment
/// variables to inject (already carrying the host-binding variables for a host-bound type), the project
/// root the working folder resolves against, the extra package dependencies to install, and the runtime
/// version to select. Fields a given type does not use are left at their defaults.
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
    string? RuntimeVersion = null);

/// <summary>
/// Builds the launch spec for one console session type, keyed by TypeId (e.g. "shell"). A new pty-only
/// type is added by registering a new provider, with no host change.
/// </summary>
public interface IConsoleSessionProvider
{
    /// <summary>
    /// The session-type key this provider handles, matched against a .console file's [session] type.
    /// </summary>
    string TypeId { get; }

    /// <summary>
    /// How this type's session reaches the host. The shell type is None (a plain pty with no host RPC).
    /// </summary>
    ConsoleHostBinding HostBinding { get; }

    /// <summary>
    /// The default runners this type contributes (file extensions plus a run-command template), or an
    /// empty list if the type runs nothing by default.
    /// </summary>
    IReadOnlyList<ConsoleRunner> DefaultRunners { get; }

    /// <summary>
    /// Builds the launch spec for a session from its resolved config, or a failure if the config cannot
    /// produce a runnable process.
    /// </summary>
    Task<Result<ConsoleLaunchSpec>> BuildLaunchSpecAsync(ConsoleSessionContext context);
}
