using StreamJsonRpc;

namespace Celbridge.Console.Services;

/// <summary>
/// The structured .console config the settings web form sends to start or restart a session, so the host
/// never parses TOML. Fields are nullable so a partial payload defaults cleanly.
/// </summary>
public sealed record ConsoleConfigDto(
    string? Type,
    string? Executable,
    IReadOnlyList<string>? Arguments,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment);

/// <summary>
/// The outcome of a console/start request: Ok on a launched session, otherwise a reason for the
/// session-failed state the document renders.
/// </summary>
public sealed record ConsoleStartResult(bool Ok, string? Error);

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
