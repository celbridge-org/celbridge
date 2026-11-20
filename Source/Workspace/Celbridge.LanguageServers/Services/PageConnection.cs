using Celbridge.LanguageServers.Helpers;
using Celbridge.Utilities;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// One page's connection to a running server, with the session's state for the page's document.
/// </summary>
internal sealed class PageConnection : ILanguagePageConnection
{
    private readonly LanguageServerSession _session;
    private bool _disposed;

    public PageConnection(LanguageServerSession session, string documentUri, string languageId)
    {
        _session = session;
        DocumentUri = documentUri;
        DocumentPath = LspUri.ToPath(documentUri);
        LanguageId = languageId;
    }

    public string DocumentUri { get; }

    /// <summary>
    /// The absolute path of the page's document.
    /// </summary>
    public string DocumentPath { get; }

    public string LanguageId { get; }

    public PageOutbox Outbox { get; } = new();

    /// <summary>
    /// True once the server has the document open. The session reads and writes the value under its lock.
    /// </summary>
    public bool IsOpen { get; set; }

    /// <summary>
    /// The version of the document's text last sent by the page to the server.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// The absolute paths of the files named by a server's answers to the page. The page may ask about
    /// these files, and about its own document. The session reads and writes the set under its lock.
    /// </summary>
    public HashSet<string> NamedFilePaths { get; } = new(PathComparison.Comparer);

    /// <summary>
    /// The result id of the document's last diagnostics, or empty before the first set.
    /// </summary>
    public string DiagnosticsResultId { get; set; } = string.Empty;

    /// <summary>
    /// The server's request id for each of the page's unanswered requests, keyed by the page's own id as JSON.
    /// </summary>
    public Dictionary<string, long> PendingRequests { get; } = new();

    public void Receive(string message)
    {
        _session.HandlePageMessage(this, message);
    }

    public ValueTask<string?> ReadNextAsync(CancellationToken cancellationToken)
    {
        return Outbox.ReadNextAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.RemovePage(this);
        Outbox.Complete();
    }
}

/// <summary>
/// The connection of a page without a language server. The page gets one message that says so, and then the
/// connection closes.
/// </summary>
internal sealed class RefusedPageConnection : ILanguagePageConnection
{
    private readonly PageOutbox _outbox = new();

    public RefusedPageConnection()
    {
        var notification = new LspNotification(PageMessages.ServerUnavailableMethod, null);
        _outbox.Enqueue(LspJson.Serialize(notification));
        _outbox.Complete();
    }

    public void Receive(string message)
    {
    }

    public ValueTask<string?> ReadNextAsync(CancellationToken cancellationToken)
    {
        return _outbox.ReadNextAsync(cancellationToken);
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// The names of the language client's own messages, outside the Language Server Protocol's methods. Mirrored by
/// language-client.js and language-features.js in the code editor.
/// </summary>
internal static class PageMessages
{
    public const string ServerReadyMethod = "celbridge/serverReady";
    public const string ServerUnavailableMethod = "celbridge/serverUnavailable";
    public const string FileTextMethod = "celbridge/fileText";
}
