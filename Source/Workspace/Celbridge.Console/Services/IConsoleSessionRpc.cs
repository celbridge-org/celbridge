using StreamJsonRpc;

namespace Celbridge.Console.Services;

/// <summary>
/// The outcome of a console/attach or console/reopen request: the session's run state
/// ("starting" | "running" | "ended" | "failed"), the failure reason when failed, whether the startup
/// phase is still pending (keep the starting veil up), the buffered output to replay, and the raw
/// .console text the session launched from (parsed client-side for the settings form's divergence check).
/// </summary>
public sealed record ConsoleAttachResult(
    string State,
    string? Error,
    bool StartupPending,
    string Replay,
    string? LaunchedConfigToml);

/// <summary>
/// JSON-RPC method names for the console channel.
/// </summary>
public static class ConsoleSessionRpcMethods
{
    // Client to host
    public const string Attach = "console/attach";
    public const string Reopen = "console/reopen";
    public const string Input = "console/input";
    public const string Submit = "console/submit";
    public const string Resize = "console/resize";

    // Host to client
    public const string Write = "console/write";
    public const string SessionState = "console/sessionState";
    public const string StartupComplete = "console/startupComplete";
}

/// <summary>
/// Inbound RPC calls the console web app makes on the channel: attach to (and render) the live session,
/// relaunch it from the file on disk, and forward terminal input and resizes.
/// </summary>
public interface IConsoleSessionRpc
{
    [JsonRpcMethod(ConsoleSessionRpcMethods.Attach)]
    Task<ConsoleAttachResult> AttachAsync(int cols, int rows);

    [JsonRpcMethod(ConsoleSessionRpcMethods.Reopen)]
    Task<ConsoleAttachResult> ReopenAsync(int cols, int rows);

    [JsonRpcMethod(ConsoleSessionRpcMethods.Input)]
    void OnInput(string data);

    [JsonRpcMethod(ConsoleSessionRpcMethods.Submit)]
    void OnSubmit(string invocation);

    [JsonRpcMethod(ConsoleSessionRpcMethods.Resize)]
    void OnResize(int cols, int rows);
}
