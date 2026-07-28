namespace Celbridge.WebHost;

/// <summary>
/// A deliberately narrow view of a custom editor's JSON-RPC host, handed to a channel. It grants only what
/// a channel needs to reach the editor — registering inbound RPC targets and sending outbound calls — and
/// withholds the rest: the WebView, the host lifecycle, and listening all stay with the controller. A
/// channel therefore cannot tear down or re-listen the host, which it shares with the editor's standard
/// document surface.
/// </summary>
public interface ICustomEditorChannelHost
{
    /// <summary>
    /// Registers a target whose methods handle inbound RPC calls from the editor. Must be called during
    /// ICustomEditorChannel.RegisterTargets, before the host starts listening.
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
