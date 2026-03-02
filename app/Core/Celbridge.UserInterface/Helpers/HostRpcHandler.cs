using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Custom IJsonRpcMessageHandler implementation that bridges WebView2's push-based
/// MessageReceived event with StreamJsonRpc's pull-based ReadAsync API.
/// Uses a Channel as a message buffer between the event-driven WebView2 and StreamJsonRpc.
/// </summary>
public class HostRpcHandler : IJsonRpcMessageHandler, IDisposable
{
    private readonly IHostChannel _channel;
    private readonly Channel<JsonRpcMessage> _incomingMessages;
    private bool _disposed;

    /// <summary>
    /// Gets the message formatter used for serialization/deserialization.
    /// </summary>
    public IJsonRpcMessageFormatter Formatter { get; }

    /// <summary>
    /// Indicates whether this handler can read messages.
    /// </summary>
    public bool CanRead => true;

    /// <summary>
    /// Indicates whether this handler can write messages.
    /// </summary>
    public bool CanWrite => true;

    /// <summary>
    /// Creates a new HostRpcHandler with the specified channel.
    /// </summary>
    public HostRpcHandler(IHostChannel channel)
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
            _incomingMessages.Writer.TryWrite(message);
        }
        catch
        {
            // Ignore malformed messages - StreamJsonRpc will handle protocol errors
        }
    }

    /// <summary>
    /// Reads the next message from the channel. Called by StreamJsonRpc.
    /// </summary>
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

    /// <summary>
    /// Writes a message to the WebView. Called by StreamJsonRpc.
    /// </summary>
    public ValueTask WriteAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>();
        Formatter.Serialize(writer, message);
        var json = Encoding.UTF8.GetString(writer.WrittenSpan);
        _channel.PostMessage(json);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Disposes the handler and releases resources.
    /// </summary>
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
