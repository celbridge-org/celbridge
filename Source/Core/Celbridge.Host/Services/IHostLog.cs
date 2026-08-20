using StreamJsonRpc;

namespace Celbridge.Host;

public static class LogRpcMethods
{
    public const string Log = "host/log";
}

/// <summary>
/// The diagnostic contract with a hosted page: one method for anything the page wants to record about
/// itself, so reporting something new never grows the protocol. Reporting is one way and best effort, and
/// what becomes of an entry is the host's business rather than the page's.
/// </summary>
public interface IHostLog
{
    /// <summary>
    /// Records one entry from the page at the given level ("debug", "info", "warn" or "error").
    /// </summary>
    [JsonRpcMethod(LogRpcMethods.Log)]
    void OnLog(string? level, string? message);
}
