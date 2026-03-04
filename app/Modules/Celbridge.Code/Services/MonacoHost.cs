using Celbridge.Host;

namespace Celbridge.Code.Services;

/// <summary>
/// Monaco-specific host facade that provides a clean API for Monaco editor RPC operations.
/// Wraps CelbridgeHost and adds Monaco-specific functionality.
/// </summary>
public class MonacoHost : IDisposable
{
    private readonly CelbridgeHost _host;
    private bool _disposed;

    public MonacoHost(CelbridgeHost host)
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
    /// Initializes the Monaco editor with the specified language.
    /// </summary>
    public Task InitializeEditorAsync(string language)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(
            MonacoRpcMethods.EditorInitialize,
            new { language });
    }

    /// <summary>
    /// Sets the language mode of the Monaco editor.
    /// </summary>
    public Task SetLanguageAsync(string language)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(
            MonacoRpcMethods.EditorSetLanguage,
            new { language });
    }

    /// <summary>
    /// Navigates to a specific location in the Monaco editor.
    /// </summary>
    public Task NavigateToLocationAsync(int lineNumber, int column)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(
            MonacoRpcMethods.EditorNavigateToLocation,
            new { lineNumber, column });
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
