namespace Celbridge.WebHost;

/// <summary>
/// Backs a custom document with a live process instead of saved content. A custom editor's standard
/// surface is content in and saves out, which cannot carry a running process's byte stream — a terminal's
/// I/O, a log tail, a debugger feed. A channel adds that stream: it owns the process backing one open
/// editor view and exchanges its own JSON-RPC methods with that view's WebView editor over the same host
/// connection. Disposed with the view.
/// </summary>
public interface ICustomEditorChannel : IDisposable
{
    /// <summary>
    /// Hands the channel the editor's host: register the RPC methods this channel handles, and keep the
    /// host to send on. The host is not yet listening, so registration must happen here. Called once.
    /// </summary>
    void RegisterTargets(ICustomEditorChannelHost host);
}
