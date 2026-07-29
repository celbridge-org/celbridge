using StreamJsonRpc;

namespace Celbridge.Console.Services;

/// <summary>
/// A runner the web app parsed from the .console config: the file extensions it handles and the command
/// template injected to run a matching file. A null or empty runner list means the session type's defaults
/// apply.
/// </summary>
public sealed record ConsoleRunnerDto(
    IReadOnlyList<string>? Extensions,
    string? Command);

/// <summary>
/// The structured .console config the settings web form sends to start or restart a session, so the host
/// never parses TOML. Fields are nullable so a partial payload defaults cleanly.
/// </summary>
public sealed record ConsoleConfigDto(
    string? Type,
    string? Title,
    string? Executable,
    string? PythonVersion,
    IReadOnlyList<string>? Arguments,
    IReadOnlyList<string>? Dependencies,
    string? WorkingDirectory,
    string? StartupScript,
    IReadOnlyDictionary<string, string>? Environment,
    IReadOnlyList<ConsoleRunnerDto>? Runners);

/// <summary>
/// The outcome of a console/start request: Ok on a launched session, otherwise a reason for the
/// session-failed state the document renders. HasStartupCommand tells the client a startup command is
/// pending injection, so it keeps the starting veil up; ReadyMarker is the text the injected command
/// echoes once it has cleared the screen, which the client watches for to reveal at the right moment
/// (null when the shell cannot emit one, leaving the client's timer to reveal).
/// </summary>
public sealed record ConsoleStartResult(
    bool Ok,
    string? Error,
    bool HasStartupCommand = false,
    string? ReadyMarker = null);

/// <summary>
/// JSON-RPC method names for the console channel.
/// </summary>
public static class ConsoleSessionRpcMethods
{
    // Client to host
    public const string Input = "console/input";
    public const string Resize = "console/resize";
    public const string Start = "console/start";

    // Host to client
    public const string Write = "console/write";
    public const string SessionState = "console/sessionState";
    public const string StartupComplete = "console/startupComplete";
}

/// <summary>
/// Inbound RPC calls the console web app makes on the channel: terminal input and resize
/// notifications, and a start request that (re)launches the pty from the current config.
/// </summary>
public interface IConsoleSessionRpc
{
    [JsonRpcMethod(ConsoleSessionRpcMethods.Input)]
    void OnInput(string data);

    [JsonRpcMethod(ConsoleSessionRpcMethods.Resize)]
    void OnResize(int cols, int rows);

    [JsonRpcMethod(ConsoleSessionRpcMethods.Start)]
    Task<ConsoleStartResult> StartAsync(int cols, int rows, ConsoleConfigDto config);
}
