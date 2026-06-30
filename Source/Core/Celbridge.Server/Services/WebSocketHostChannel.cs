using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Celbridge.Host;

namespace Celbridge.Server.Services;

/// <summary>
/// IHostChannel implementation over a WebSocket accepted on the loopback server. Outbound messages are
/// drained by a single send pump so they are written in order and never overlap, and the receive loop
/// reassembles multi-frame text messages before raising MessageReceived.
/// </summary>
public sealed class WebSocketHostChannel : IHostChannel, IDisposable
{
    private readonly WebSocket _webSocket;
    private readonly Channel<string> _outbound;
    private readonly ILogger<WebSocketHostChannel> _logger;
    private bool _disposed;

    public event EventHandler<string>? MessageReceived;

    public WebSocketHostChannel(WebSocket webSocket)
    {
        _webSocket = webSocket;
        _logger = ServiceLocator.AcquireService<ILogger<WebSocketHostChannel>>();

        var outboundOptions = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        };
        _outbound = Channel.CreateUnbounded<string>(outboundOptions);
    }

    public void PostMessage(string json)
    {
        _outbound.Writer.TryWrite(json);
    }

    /// <summary>
    /// Runs the send pump and receive loop until the socket closes or the request aborts. The endpoint
    /// awaits this to keep the WebSocket request alive for the lifetime of the connection.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var sendPumpTask = Task.Run(() => RunSendPumpAsync(cancellationToken), CancellationToken.None);

        try
        {
            await RunReceiveLoopAsync(cancellationToken);
        }
        finally
        {
            // Stop the send pump and let any in-flight send finish before the socket is disposed.
            _outbound.Writer.TryComplete();
            try
            {
                await sendPumpTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WebSocket host channel send pump ended with an error");
            }
        }
    }

    private async Task RunSendPumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var json in _outbound.Reader.ReadAllAsync(cancellationToken))
            {
                if (_webSocket.State != WebSocketState.Open)
                {
                    continue;
                }

                var bytes = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(
                    new ReadOnlyMemory<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogDebug(ex, "WebSocket host channel send pump ended");
        }
    }

    private async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var receiveBuffer = new byte[8192];
        using var messageBuffer = new MemoryStream();

        try
        {
            while (_webSocket.State == WebSocketState.Open
                && !cancellationToken.IsCancellationRequested)
            {
                messageBuffer.SetLength(0);

                WebSocketReceiveResult receiveResult;
                do
                {
                    receiveResult = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(receiveBuffer),
                        cancellationToken);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseAsync();
                        return;
                    }

                    messageBuffer.Write(receiveBuffer, 0, receiveResult.Count);
                }
                while (!receiveResult.EndOfMessage);

                var json = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                if (!string.IsNullOrEmpty(json))
                {
                    MessageReceived?.Invoke(this, json);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
            // The view tore down and disposed the socket while a receive was in flight: expected.
        }
        catch (WebSocketException ex)
        {
            // A client that closes its tab drops the socket without a close handshake. This is expected.
            _logger.LogDebug(ex, "WebSocket host channel receive loop ended");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error in the WebSocket host channel receive loop");
        }
    }

    private async Task CloseAsync()
    {
        try
        {
            if (_webSocket.State == WebSocketState.Open
                || _webSocket.State == WebSocketState.CloseReceived)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to close the WebSocket host channel cleanly");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _outbound.Writer.TryComplete();
        _webSocket.Dispose();
    }
}
