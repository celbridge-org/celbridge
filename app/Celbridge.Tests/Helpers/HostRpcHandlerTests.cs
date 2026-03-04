using Celbridge.Host;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace Celbridge.Tests.Helpers;

/// <summary>
/// Tests for HostRpcHandler's unique adapter behavior.
/// These tests focus on the Channel buffering and IJsonRpcMessageHandler implementation,
/// not the full request/response cycle (which is covered by CelbridgeHostTests).
/// </summary>
[TestFixture]
public class HostRpcHandlerTests
{
    private MockHostChannel _channel = null!;
    private HostRpcHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _channel = new MockHostChannel();
        _handler = new HostRpcHandler(_channel);
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
    }

    [Test]
    public async Task ReadAsync_ReturnsMessage_WhenMessageReceived()
    {
        // Arrange - simulate a JSON-RPC message
        var json = """{"jsonrpc":"2.0","method":"test/method","id":1}""";

        // Act
        _channel.SimulateMessage(json);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var message = await _handler.ReadAsync(cts.Token);

        // Assert
        message.Should().NotBeNull();
        message.Should().BeAssignableTo<JsonRpcRequest>();
        var request = (JsonRpcRequest)message!;
        request.Method.Should().Be("test/method");
    }

    [Test]
    public async Task ReadAsync_ReturnsMessagesInOrder()
    {
        // Arrange - simulate multiple messages
        _channel.SimulateMessage("""{"jsonrpc":"2.0","method":"first","id":1}""");
        _channel.SimulateMessage("""{"jsonrpc":"2.0","method":"second","id":2}""");
        _channel.SimulateMessage("""{"jsonrpc":"2.0","method":"third","id":3}""");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act & Assert - messages should arrive in order
        var first = (JsonRpcRequest)(await _handler.ReadAsync(cts.Token))!;
        first.Method.Should().Be("first");

        var second = (JsonRpcRequest)(await _handler.ReadAsync(cts.Token))!;
        second.Method.Should().Be("second");

        var third = (JsonRpcRequest)(await _handler.ReadAsync(cts.Token))!;
        third.Method.Should().Be("third");
    }

    [Test]
    public async Task ReadAsync_ReturnsNull_WhenCancelledAfterDispose()
    {
        // Arrange
        _handler.Dispose();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var message = await _handler.ReadAsync(cts.Token);

        // Assert - disposed handler returns null
        message.Should().BeNull();
    }

    [Test]
    public void Dispose_UnsubscribesFromChannelMessages()
    {
        // Arrange - start a read task before dispose
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var readTask = _handler.ReadAsync(cts.Token);

        // Act
        _handler.Dispose();

        // Simulate a message after dispose
        _channel.SimulateMessage("""{"jsonrpc":"2.0","method":"test","id":1}""");

        // Assert - the read task should complete with null (channel closed)
        Func<Task> act = async () =>
        {
            var result = await readTask;
            result.Should().BeNull();
        };
        act.Should().NotThrowAsync();
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Act & Assert - should not throw
        _handler.Dispose();
        _handler.Dispose();
        _handler.Dispose();
    }

    [Test]
    public void CanRead_ReturnsTrue()
    {
        _handler.CanRead.Should().BeTrue();
    }

    [Test]
    public void CanWrite_ReturnsTrue()
    {
        _handler.CanWrite.Should().BeTrue();
    }

    [Test]
    public void Formatter_IsConfiguredForJavaScriptInterop()
    {
        // Assert - formatter should be configured
        _handler.Formatter.Should().NotBeNull();
        _handler.Formatter.Should().BeOfType<SystemTextJsonFormatter>();
    }
}
