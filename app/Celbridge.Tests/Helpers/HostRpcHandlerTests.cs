using System.Text.Json;
using Celbridge.UserInterface.Helpers;
using StreamJsonRpc;

namespace Celbridge.Tests.Helpers;

[TestFixture]
public class HostRpcHandlerTests
{
    private MockHostChannel _channel = null!;
    private HostRpcHandler _handler = null!;
    private JsonRpc _rpc = null!;

    // Options to enable JsonRpcMethod attribute-based method name mapping
    private static readonly JsonRpcTargetOptions RpcTargetOptions = new()
    {
        MethodNameTransform = CommonMethodNameTransforms.CamelCase,
        UseSingleObjectParameterDeserialization = true
    };

    [SetUp]
    public void SetUp()
    {
        _channel = new MockHostChannel();
        _handler = new HostRpcHandler(_channel);
        _rpc = new JsonRpc(_handler);
    }

    [TearDown]
    public void TearDown()
    {
        _rpc.Dispose();
        _handler.Dispose();
    }

    [Test]
    public async Task HandleRequest_WithRegisteredTarget_CallsHandlerAndSendsResponse()
    {
        // Arrange
        var service = new TestHostInit();
        _rpc.AddLocalRpcTarget<IHostInit>(service, RpcTargetOptions);
        _rpc.StartListening();

        // Act
        _channel.SimulateRequest(1, HostRpcMethods.Initialize, new { protocolVersion = "1.0" });

        // Allow async processing
        await Task.Delay(100);

        // Assert
        service.InitializeCalled.Should().BeTrue();
        _channel.SentMessages.Should().HaveCount(1);

        var response = JsonDocument.Parse(_channel.SentMessages[0]);
        var root = response.RootElement;
        root.GetProperty("id").GetInt32().Should().Be(1);
        root.TryGetProperty("result", out _).Should().BeTrue();
    }

    [Test]
    public async Task HandleRequest_MethodNotFound_SendsErrorResponse()
    {
        // Arrange
        _rpc.StartListening();

        // Act
        _channel.SimulateRequest(1, "unknown/method", null);

        // Allow async processing
        await Task.Delay(100);

        // Assert
        _channel.SentMessages.Should().HaveCount(1);

        var response = JsonDocument.Parse(_channel.SentMessages[0]);
        var root = response.RootElement;
        root.GetProperty("id").GetInt32().Should().Be(1);
        root.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").GetInt32().Should().Be(-32601); // Method not found
    }

    [Test]
    public async Task HandleNotification_WithRegisteredTarget_CallsHandler()
    {
        // Arrange
        var service = new TestHostNotifications();
        _rpc.AddLocalRpcTarget<IHostNotifications>(service, RpcTargetOptions);
        _rpc.StartListening();

        // Act
        _channel.SimulateNotification(HostRpcMethods.DocumentChanged, null);

        // Allow async processing
        await Task.Delay(100);

        // Assert
        service.DocumentChangedCalled.Should().BeTrue();
        _channel.SentMessages.Should().BeEmpty(); // Notifications don't get responses
    }

    [Test]
    public async Task SendNotification_SendsCorrectJsonRpcFormat()
    {
        // Arrange
        _rpc.StartListening();

        // Act
        await _rpc.NotifyExternalChangeAsync();

        // Assert
        _channel.SentMessages.Should().HaveCount(1);

        var notification = JsonDocument.Parse(_channel.SentMessages[0]);
        var root = notification.RootElement;
        root.GetProperty("method").GetString().Should().Be(HostRpcMethods.DocumentExternalChange);
        root.TryGetProperty("id", out _).Should().BeFalse(); // Notifications have no id
    }

    [Test]
    public void Dispose_UnsubscribesFromMessages()
    {
        // Arrange
        var service = new TestHostNotifications();
        _rpc.AddLocalRpcTarget<IHostNotifications>(service, RpcTargetOptions);
        _rpc.StartListening();

        // Act
        _handler.Dispose();
        _channel.SimulateNotification(HostRpcMethods.DocumentChanged, null);

        // Assert - handler should not be called after dispose
        service.DocumentChangedCalled.Should().BeFalse();
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
    public void Formatter_IsNotNull()
    {
        _handler.Formatter.Should().NotBeNull();
    }

    /// <summary>
    /// Test implementation of IHostInit.
    /// </summary>
    private class TestHostInit : IHostInit
    {
        public bool InitializeCalled { get; private set; }

        public Task<InitializeResult> InitializeAsync(string protocolVersion)
        {
            InitializeCalled = true;
            var metadata = new DocumentMetadata("/path/test.md", "test", "test.md");
            var localization = new Dictionary<string, string> { { "key", "value" } };
            var result = new InitializeResult("# Content", metadata, localization);
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Test implementation of IHostNotifications.
    /// </summary>
    private class TestHostNotifications : IHostNotifications
    {
        public bool DocumentChangedCalled { get; private set; }
        public bool LinkClickedCalled { get; private set; }
        public bool ImportCompleteCalled { get; private set; }

        public void OnDocumentChanged()
        {
            DocumentChangedCalled = true;
        }

        public void OnLinkClicked(string href)
        {
            LinkClickedCalled = true;
        }

        public void OnImportComplete(bool success, string? error = null)
        {
            ImportCompleteCalled = true;
        }
    }
}
