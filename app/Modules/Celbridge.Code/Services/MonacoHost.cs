using Celbridge.Host;

namespace Celbridge.Code.Services;

/// <summary>
/// JSON-RPC method names for Monaco editor operations (host to client).
/// </summary>
internal static class MonacoRpcMethods
{
    public const string EditorInitialize = "editor/initialize";
    public const string EditorSetLanguage = "editor/setLanguage";
    public const string EditorNavigateToLocation = "editor/navigateToLocation";
    public const string EditorScrollToPercentage = "editor/scrollToPercentage";
}

/// <summary>
/// Monaco-specific host facade that provides a clean API for Monaco editor RPC operations.
/// Wraps CelbridgeHost and uses JSON-RPC notifications for editor communication.
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
    /// Initializes the Monaco editor with the specified language and options.
    /// </summary>
    public Task InitializeEditorAsync(string language, bool scrollBeyondLastLine = true)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(MonacoRpcMethods.EditorInitialize, new 
        { 
            language,
            scrollBeyondLastLine
        });
    }

    /// <summary>
    /// Sets the language mode of the Monaco editor.
    /// </summary>
    public Task SetLanguageAsync(string language)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(MonacoRpcMethods.EditorSetLanguage, new { language });
    }

    /// <summary>
    /// Navigates to a specific location in the Monaco editor.
    /// </summary>
    public Task NavigateToLocationAsync(int lineNumber, int column)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(MonacoRpcMethods.EditorNavigateToLocation, new { lineNumber, column });
    }

    /// <summary>
    /// Scrolls the Monaco editor to a specific percentage position.
    /// </summary>
    public Task ScrollToPercentageAsync(double percentage)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(MonacoRpcMethods.EditorScrollToPercentage, new { percentage });
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
