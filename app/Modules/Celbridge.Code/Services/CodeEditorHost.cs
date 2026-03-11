using Celbridge.Documents;
using Celbridge.Host;

namespace Celbridge.Code.Services;

/// <summary>
/// JSON-RPC method names for code editor operations (host to client).
/// </summary>
internal static class CodeEditorRpcMethods
{
    public const string Initialize = "codeEditor/initialize";
    public const string SetLanguage = "codeEditor/setLanguage";
    public const string NavigateToLocation = "codeEditor/navigateToLocation";
    public const string ScrollToPercentage = "codeEditor/scrollToPercentage";
    public const string InsertText = "codeEditor/insertText";
    public const string ApplyEdits = "codeEditor/applyEdits";
}

/// <summary>
/// Host facade that provides a clean API for code editor RPC operations.
/// Wraps CelbridgeHost and uses JSON-RPC notifications for editor communication.
/// </summary>
public class CodeEditorHost : IDisposable
{
    private readonly CelbridgeHost _host;
    private bool _disposed;

    public CodeEditorHost(CelbridgeHost host)
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
    /// Initializes the code editor with the specified language and options.
    /// </summary>
    public Task InitializeEditorAsync(string language, bool scrollBeyondLastLine = true)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(CodeEditorRpcMethods.Initialize, new
        { 
            language,
            scrollBeyondLastLine
        });
    }

    /// <summary>
    /// Sets the language mode of the code editor.
    /// </summary>
    public Task SetLanguageAsync(string language)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(CodeEditorRpcMethods.SetLanguage, new { language });
    }

    /// <summary>
    /// Navigates to a specific location in the code editor.
    /// </summary>
    public Task NavigateToLocationAsync(int lineNumber, int column, int endLineNumber = 0, int endColumn = 0)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(CodeEditorRpcMethods.NavigateToLocation, new { lineNumber, column, endLineNumber, endColumn });
    }

    /// <summary>
    /// Scrolls the code editor to a specific percentage position.
    /// </summary>
    public Task ScrollToPercentageAsync(double percentage)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(CodeEditorRpcMethods.ScrollToPercentage, new { percentage });
    }

    /// <summary>
    /// Inserts text at the current cursor position (or replaces the current selection).
    /// </summary>
    public Task InsertTextAsync(string text)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(CodeEditorRpcMethods.InsertText, new { text });
    }

    /// <summary>
    /// Applies a batch of text edits to the Monaco editor as a single undo unit.
    /// </summary>
    public Task ApplyEditsAsync(IEnumerable<TextEdit> edits)
    {
        // Convert to camelCase property names for Monaco editor
        var monacoEdits = edits.Select(e => new
        {
            line = e.Line,
            column = e.Column,
            endLine = e.EndLine,
            endColumn = e.EndColumn,
            newText = e.NewText
        });

        return _host.Rpc.NotifyWithParameterObjectAsync(CodeEditorRpcMethods.ApplyEdits, new { edits = monacoEdits });
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
