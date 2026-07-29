namespace Celbridge.WebHost;

/// <summary>
/// A deliberately narrow view of a custom editor's JSON-RPC host, handed to a channel. It grants only what
/// a channel needs to reach the editor: registering inbound RPC targets and sending outbound calls.
/// </summary>
public interface ICustomEditorChannelHost
{
    /// <summary>
    /// Registers a target whose methods handle inbound RPC calls from the editor. Must be called before the
    /// host starts listening.
    /// </summary>
    void AddLocalRpcTarget<T>(T target) where T : class;

    /// <summary>
    /// Sends a fire-and-forget notification to the editor. A null argument sends the method with no
    /// parameters. No-ops once the channel has been torn down.
    /// </summary>
    Task NotifyAsync(string method, object? argument);

    /// <summary>
    /// Invokes a request on the editor and awaits its result. Faults once the channel has been torn down.
    /// </summary>
    Task<T> InvokeAsync<T>(string method);
}
