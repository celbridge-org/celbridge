namespace Celbridge.LanguageServers;

/// <summary>
/// One code editor page's connection to the language server of the page's document. Each message is one JSON
/// object, in the Language Server Protocol's shapes.
/// </summary>
public interface ILanguagePageConnection : IDisposable
{
    /// <summary>
    /// Handles a message that the page sent.
    /// </summary>
    void Receive(string message);

    /// <summary>
    /// Returns the next message for the page. Returns null once the connection has closed.
    /// </summary>
    ValueTask<string?> ReadNextAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Runs the language servers of the loaded project, and connects code editor pages to them. Safe to call from any
/// thread.
/// </summary>
public interface ILanguageServerService
{
    /// <summary>
    /// Returns the id of the language server that covers the document. Returns an empty string when no server
    /// covers the document.
    /// </summary>
    string GetServerId(ResourceKey resource);

    /// <summary>
    /// Connects a page to the language server that covers the page's document, and starts the server if needed.
    /// The connection closes at once when no server covers the document.
    /// </summary>
    ILanguagePageConnection ConnectPage(ResourceKey resource);
}
