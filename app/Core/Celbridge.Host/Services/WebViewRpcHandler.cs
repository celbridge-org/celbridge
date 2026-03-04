using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace Celbridge.Host;

/// <summary>
/// IJsonRpcMessageHandler implementation that bridges WebView2's push-based
/// MessageReceived event with StreamJsonRpc's pull-based ReadAsync API.
/// Uses a Channel as a message buffer between the event-driven WebView2 and StreamJsonRpc.
/// </summary>
internal class WebViewRpcHandler : IJsonRpcMessageHandler, IDisposable
{
    private readonly IHostChannel _channel;
    private readonly Channel<JsonRpcMessage> _incomingMessages;
    private bool _disposed;

    public IJsonRpcMessageFormatter Formatter { get; }
    public bool CanRead => true;
    public bool CanWrite => true;

    public WebViewRpcHandler(IHostChannel channel)
    {
        _channel = channel;

        // Configure JSON serialization for JavaScript interop (camelCase)
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        Formatter = new SystemTextJsonFormatter { JsonSerializerOptions = jsonOptions };

        var options = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        };
        _incomingMessages = Channel.CreateUnbounded<JsonRpcMessage>(options);

        _channel.MessageReceived += OnMessageReceived;
    }

    private void OnMessageReceived(object? sender, string json)
    {
        try
        {
            var sequence = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(json));
            var message = Formatter.Deserialize(sequence);
            if (message != null)
            {
                _incomingMessages.Writer.TryWrite(message);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebViewRpcHandler] Failed to deserialize message: {ex.Message}");
        }
    }

    public async ValueTask<JsonRpcMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _incomingMessages.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public ValueTask WriteAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>();
        Formatter.Serialize(writer, message);
        var json = Encoding.UTF8.GetString(writer.WrittenSpan);
        _channel.PostMessage(json);
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.MessageReceived -= OnMessageReceived;
        _incomingMessages.Writer.TryComplete();
    }
}
