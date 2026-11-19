using Celbridge.Logging;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// The language channel of one code editor view. The channel connects the view's page to the language server of
/// the page's document, over the page's host connection. The page's messages go to the connection, and the
/// connection's messages go to the page.
/// </summary>
internal sealed class LanguageChannel : ICustomEditorChannel, ILanguageChannelRpc
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogger _logger;

    private readonly Lock _lock = new();
    private ICustomEditorChannelHost? _host;
    private ILanguagePageConnection? _connection;
    private CancellationTokenSource? _readCancellation;
    private bool _disposed;

    public LanguageChannel(IWorkspaceWrapper workspaceWrapper, ILogger logger)
    {
        _workspaceWrapper = workspaceWrapper;
        _logger = logger;
    }

    public void RegisterTargets(ICustomEditorChannelHost host)
    {
        _host = host;
        host.AddLocalRpcTarget<ILanguageChannelRpc>(this);
    }

    public void OnOpen()
    {
        CloseConnection();

        var host = _host;
        if (host is null
            || !_workspaceWrapper.HasWorkspaceService)
        {
            return;
        }

        // Renaming the document changes the host's resource, so each open reads the resource again.
        var languageServerService = _workspaceWrapper.WorkspaceService.LanguageServerService;
        var connection = languageServerService.ConnectPage(host.Resource);
        var readCancellation = new CancellationTokenSource();

        lock (_lock)
        {
            if (_disposed)
            {
                connection.Dispose();
                readCancellation.Dispose();
                return;
            }

            _connection = connection;
            _readCancellation = readCancellation;
        }

        _ = Task.Run(() => SendConnectionMessagesAsync(host, connection, readCancellation.Token));
    }

    public void OnSend(string message)
    {
        ILanguagePageConnection? connection;
        lock (_lock)
        {
            connection = _connection;
        }

        connection?.Receive(message);
    }

    public void OnClose()
    {
        CloseConnection();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }

        CloseConnection();
    }

    // Closes the open connection, and stops passing the connection's messages to the page.
    private void CloseConnection()
    {
        ILanguagePageConnection? connection;
        CancellationTokenSource? readCancellation;

        lock (_lock)
        {
            connection = _connection;
            readCancellation = _readCancellation;
            _connection = null;
            _readCancellation = null;
        }

        readCancellation?.Cancel();
        connection?.Dispose();
        readCancellation?.Dispose();
    }

    // Passes the connection's messages to the page, in order. When a server stops, the page gets
    // `language/closed`. When the channel itself closes the connection, the page gets no message.
    private async Task SendConnectionMessagesAsync(
        ICustomEditorChannelHost host,
        ILanguagePageConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var message = await connection.ReadNextAsync(cancellationToken);
                if (message is null)
                {
                    break;
                }

                await host.NotifyAsync(LanguageChannelRpcMethods.Message, new { message });
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await host.NotifyAsync(LanguageChannelRpcMethods.Closed, new { });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to pass a language server's messages to a page");
        }
    }
}
