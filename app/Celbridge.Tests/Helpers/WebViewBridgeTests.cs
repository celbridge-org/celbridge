using System.Text.Json;
using Celbridge.UserInterface.Helpers;

namespace Celbridge.Tests.Helpers;

/// <summary>
/// Mock implementation of IWebViewMessageChannel for testing.
/// </summary>
public class MockWebViewMessageChannel : IWebViewMessageChannel
{
    public List<string> SentMessages { get; } = new();

    public event EventHandler<string>? MessageReceived;

    public void PostMessage(string json)
    {
        SentMessages.Add(json);
    }

    public void SimulateMessage(string json)
    {
        MessageReceived?.Invoke(this, json);
    }

    public void SimulateRequest(int id, string method, object? parameters = null)
    {
        var request = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters,
            id
        };
        SimulateMessage(JsonSerializer.Serialize(request));
    }

    public void SimulateNotification(string method, object? parameters = null)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        };
        SimulateMessage(JsonSerializer.Serialize(notification));
    }
}

[TestFixture]
public class WebViewBridgeTests
{
    private MockWebViewMessageChannel _channel = null!;
    private WebViewBridge _bridge = null!;

    [SetUp]
    public void SetUp()
    {
        _channel = new MockWebViewMessageChannel();
        _bridge = new WebViewBridge(_channel);
    }

    [TearDown]
    public void TearDown()
    {
        _bridge.Dispose();
    }

    [Test]
    public async Task HandleRequest_CallsRegisteredHandler_AndSendsResponse()
    {
        // Arrange
        var handlerCalled = false;
        _bridge.OnInitialize(async (request) =>
        {
            handlerCalled = true;
            request.ProtocolVersion.Should().Be("1.0");
            return new InitializeResult(
                "# Content",
                new DocumentMetadata("/path/test.md", "test", "test.md"),
                new Dictionary<string, string> { { "key", "value" } },
                new ThemeInfo("Dark", true));
        });

        // Act
        _channel.SimulateRequest(1, "bridge/initialize", new { protocolVersion = "1.0" });

        // Allow async handler to complete
        await Task.Delay(50);

        // Assert
        handlerCalled.Should().BeTrue();
        _channel.SentMessages.Should().HaveCount(1);

        var response = JsonDocument.Parse(_channel.SentMessages[0]);
        var root = response.RootElement;
        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("id").GetInt32().Should().Be(1);
        root.GetProperty("result").GetProperty("content").GetString().Should().Be("# Content");
    }

    [Test]
    public async Task HandleRequest_MethodNotFound_SendsErrorResponse()
    {
        // Act
        _channel.SimulateRequest(1, "unknown/method", null);

        await Task.Delay(50);

        // Assert
        _channel.SentMessages.Should().HaveCount(1);

        var response = JsonDocument.Parse(_channel.SentMessages[0]);
        var root = response.RootElement;
        root.GetProperty("id").GetInt32().Should().Be(1);
        var error = root.GetProperty("error");
        error.GetProperty("code").GetInt32().Should().Be(JsonRpcErrorCodes.MethodNotFound);
        error.GetProperty("message").GetString().Should().Contain("Method not found");
    }

    [Test]
    public async Task HandleRequest_HandlerThrowsBridgeException_SendsErrorWithCode()
    {
        // Arrange
        _bridge.OnInitialize(async (request) =>
        {
            throw new BridgeException(JsonRpcErrorCodes.InvalidVersion, "Unsupported version", new { expected = "1.0" });
        });

        // Act
        _channel.SimulateRequest(1, "bridge/initialize", new { protocolVersion = "0.5" });

        await Task.Delay(50);

        // Assert
        var response = JsonDocument.Parse(_channel.SentMessages[0]);
        var error = response.RootElement.GetProperty("error");
        error.GetProperty("code").GetInt32().Should().Be(JsonRpcErrorCodes.InvalidVersion);
        error.GetProperty("message").GetString().Should().Be("Unsupported version");
    }

    [Test]
    public async Task HandleRequest_HandlerThrowsException_SendsInternalError()
    {
        // Arrange
        _bridge.OnInitialize(async (request) =>
        {
            throw new InvalidOperationException("Something went wrong");
        });

        // Act
        _channel.SimulateRequest(1, "bridge/initialize", new { protocolVersion = "1.0" });

        await Task.Delay(50);

        // Assert
        var response = JsonDocument.Parse(_channel.SentMessages[0]);
        var error = response.RootElement.GetProperty("error");
        error.GetProperty("code").GetInt32().Should().Be(JsonRpcErrorCodes.InternalError);
        error.GetProperty("message").GetString().Should().Contain("Something went wrong");
    }

    [Test]
    public async Task HandleNotification_CallsRegisteredHandler()
    {
        // Arrange
        var notificationReceived = false;
        _bridge.Document.OnChanged(() =>
        {
            notificationReceived = true;
        });

        // Act
        _channel.SimulateNotification("document/changed", new { });

        await Task.Delay(50);

        // Assert
        notificationReceived.Should().BeTrue();
        _channel.SentMessages.Should().BeEmpty(); // Notifications don't get responses
    }

    [Test]
    public void SendNotification_SendsCorrectJsonRpcFormat()
    {
        // Act
        _bridge.Document.NotifyExternalChange();

        // Assert
        _channel.SentMessages.Should().HaveCount(1);

        var notification = JsonDocument.Parse(_channel.SentMessages[0]);
        var root = notification.RootElement;
        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("method").GetString().Should().Be("document/externalChange");
        root.TryGetProperty("id", out _).Should().BeFalse(); // Notifications have no id
    }

    [Test]
    public async Task DocumentOnSave_HandlerReceivesContent()
    {
        // Arrange
        string? savedContent = null;
        _bridge.Document.OnSave(async (request) =>
        {
            savedContent = request.Content;
            return new SaveResult(true);
        });

        // Act
        _channel.SimulateRequest(1, "document/save", new { content = "# Hello World" });

        await Task.Delay(50);

        // Assert
        savedContent.Should().Be("# Hello World");
    }

    [Test]
    public async Task DocumentOnLoad_ReturnsContent()
    {
        // Arrange
        _bridge.Document.OnLoad(async (request) =>
        {
            return new LoadResult("# Loaded content");
        });

        // Act
        _channel.SimulateRequest(1, "document/load", new { includeMetadata = false });

        await Task.Delay(50);

        // Assert
        var response = JsonDocument.Parse(_channel.SentMessages[0]);
        response.RootElement.GetProperty("result").GetProperty("content").GetString()
            .Should().Be("# Loaded content");
    }

    [Test]
    public async Task DialogOnPickImage_ReturnsPath()
    {
        // Arrange
        _bridge.Dialog.OnPickImage(async (request) =>
        {
            request.Extensions.Should().Contain(".png");
            return new PickImageResult("/images/photo.png");
        });

        // Act
        _channel.SimulateRequest(1, "dialog/pickImage", new { extensions = new[] { ".png", ".jpg" } });

        await Task.Delay(50);

        // Assert
        var response = JsonDocument.Parse(_channel.SentMessages[0]);
        response.RootElement.GetProperty("result").GetProperty("path").GetString()
            .Should().Be("/images/photo.png");
    }

    [Test]
    public async Task DialogOnAlert_Completes()
    {
        // Arrange
        string? alertTitle = null;
        string? alertMessage = null;
        _bridge.Dialog.OnAlert(async (request) =>
        {
            alertTitle = request.Title;
            alertMessage = request.Message;
            return new AlertResult();
        });

        // Act
        _channel.SimulateRequest(1, "dialog/alert", new { title = "Warning", message = "Something happened" });

        await Task.Delay(50);

        // Assert
        alertTitle.Should().Be("Warning");
        alertMessage.Should().Be("Something happened");
        _channel.SentMessages.Should().HaveCount(1);
    }

    [Test]
    public void InvalidJson_DoesNotThrow()
    {
        // Act & Assert - should not throw
        _channel.SimulateMessage("{ invalid json");
    }

    [Test]
    public void MissingMethod_SendsErrorForRequestWithId()
    {
        // Act
        _channel.SimulateMessage("""{"jsonrpc":"2.0","id":1}""");

        // Assert - should send error response
        _channel.SentMessages.Should().HaveCount(1);
        var response = JsonDocument.Parse(_channel.SentMessages[0]);
        response.RootElement.GetProperty("error").GetProperty("code").GetInt32()
            .Should().Be(JsonRpcErrorCodes.InvalidRequest);
    }

    [Test]
    public void Dispose_UnsubscribesFromMessages()
    {
        // Arrange
        var handlerCalled = false;
        _bridge.Document.OnChanged(() => handlerCalled = true);

        // Act
        _bridge.Dispose();
        _channel.SimulateNotification("document/changed", new { });

        // Assert
        handlerCalled.Should().BeFalse();
    }
}
