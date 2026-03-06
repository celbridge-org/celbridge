using Celbridge.Host;

namespace Celbridge.Notes.Services;

/// <summary>
/// JSON-RPC method names for Note editor operations (host to client).
/// </summary>
internal static class NoteRpcMethods
{
    public const string NavigateToHeading = "note/navigateToHeading";
    public const string SetTocVisibility = "note/setTocVisibility";
    public const string Focus = "note/focus";
}

/// <summary>
/// Note-specific host facade that provides a clean API for Note editor RPC operations.
/// Wraps CelbridgeHost and uses JSON-RPC notifications for editor communication.
/// </summary>
public class NoteHost : IDisposable
{
    private readonly CelbridgeHost _host;
    private bool _disposed;

    public NoteHost(CelbridgeHost host)
    {
        _host = host;
    }

    /// <summary>
    /// Registers a target object that implements RPC methods.
    /// </summary>
    public void AddLocalRpcTarget<T>(T target) where T : class
    {
        _host.AddLocalRpcTarget(target);
    }

    /// <summary>
    /// Starts listening for incoming RPC messages.
    /// </summary>
    public void StartListening()
    {
        _host.StartListening();
    }

    /// <summary>
    /// Requests the WebView to save the current document content.
    /// </summary>
    public Task NotifyRequestSaveAsync()
    {
        return _host.NotifyRequestSaveAsync();
    }

    /// <summary>
    /// Notifies the WebView that the document has been externally modified.
    /// </summary>
    public Task NotifyExternalChangeAsync()
    {
        return _host.NotifyExternalChangeAsync();
    }

    /// <summary>
    /// Navigates to a specific heading in the Note editor.
    /// </summary>
    public Task NavigateToHeadingAsync(string heading)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(NoteRpcMethods.NavigateToHeading, new { heading });
    }

    /// <summary>
    /// Sets the visibility of the Table of Contents panel.
    /// </summary>
    public Task SetTocVisibilityAsync(bool visible)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(NoteRpcMethods.SetTocVisibility, new { visible });
    }

    /// <summary>
    /// Focuses the Note editor.
    /// </summary>
    public Task FocusAsync()
    {
        return _host.Rpc.NotifyAsync(NoteRpcMethods.Focus);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.Dispose();
    }
}
