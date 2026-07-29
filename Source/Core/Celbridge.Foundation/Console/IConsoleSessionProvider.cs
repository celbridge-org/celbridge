namespace Celbridge.Console;

/// <summary>
/// The resolved inputs to start a console session's process. The environment variables carry only the
/// deltas the pty backend merges into the process environment.
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
/// The resolved configuration a provider builds a launch spec from. WorkingDirectory is as written in the
/// config and resolves against ProjectFolderPath, and the environment variables already carry the RPC port
/// and session token. Fields a given type does not use are left at their defaults.
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
/// Builds the launch spec for one console session type, keyed by TypeId (e.g. "shell").
/// </summary>
public interface IConsoleSessionProvider
{
    /// <summary>
    /// The session-type key this provider handles, matched against a .console file's [session] type.
    /// </summary>
    string TypeId { get; }

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
