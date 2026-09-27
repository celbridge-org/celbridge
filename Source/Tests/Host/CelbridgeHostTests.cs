using System.Text.Json;
using Celbridge.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Celbridge.Tests.Host;

/// <summary>
/// Tests for CelbridgeHost facade behavior.
/// </summary>
[TestFixture]
public class CelbridgeHostTests
{
    private MockHostChannel _channel = null!;
    private CelbridgeHost _host = null!;
    private IServiceProvider? _previousServiceProvider;

    [SetUp]
    public void SetUp()
    {
        // RpcMessageHandler acquires its logger through the global ServiceLocator, so the locator must
        // be initialized with the Celbridge logger registration before a CelbridgeHost is constructed.
        // The previous provider is captured and restored in TearDown so this fixture leaves the global
        // ServiceLocator exactly as it found it (other fixtures inherit it).
        _previousServiceProvider = ServiceLocator.ServiceProvider;

        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Celbridge.Logging.Services.Logger<>)));
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _channel = new MockHostChannel();
        _host = new CelbridgeHost(_channel, new StubHostLog());
    }

    // The host registers a log target for every page it hosts, so constructing one needs somewhere for the
    // page's diagnostics to go. These tests do not assert on them.
    private sealed class StubHostLog : IHostLog
    {
        public void OnLog(string? level, string? message)
        {
        }
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();

        if (_previousServiceProvider is not null)
        {
            ServiceLocator.Initialize(_previousServiceProvider);
        }
        else
        {
            ServiceLocator.Reset();
        }
    }

    [Test]
    public void Rpc_IsNotNull_AfterConstruction()
    {
        _host.Rpc.Should().NotBeNull();
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Act & Assert - should not throw
        _host.Dispose();
        _host.Dispose();
        _host.Dispose();
    }

    [Test]
    public async Task NotifyRequestSaveAsync_SendsCorrectMethod()
    {
        // Arrange
        _host.StartListening();

        // Act
        await _host.NotifyRequestSaveAsync();

        // Assert
        _channel.SentMessages.Should().HaveCount(1);
        _channel.SentMessages[0].Should().Contain("document/requestSave");
    }

    [Test]
    public async Task NotifyExternalChangeAsync_SendsCorrectMethod()
    {
        // Arrange
        _host.StartListening();

        // Act
        await _host.NotifyExternalChangeAsync(preserveViewState: true);

        // Assert
        _channel.SentMessages.Should().HaveCount(1);
        _channel.SentMessages[0].Should().Contain("document/externalChange");
        _channel.SentMessages[0].Should().Contain("preserveViewState");
    }

    [Test]
    public async Task NotifyRenamedAsync_SendsTheNewNameAndPath()
    {
        _host.StartListening();
        var metadata = new DocumentMetadata(@"C:\Acme\site\renamed.html", "project:site/renamed.html", "renamed.html", "en");

        await _host.NotifyRenamedAsync(metadata);

        // The page reads the metadata in the same shape it gets when the document opens.
        _channel.SentMessages.Should().HaveCount(1);
        using var message = JsonDocument.Parse(_channel.SentMessages[0]);
        message.RootElement.GetProperty("method").GetString().Should().Be("document/renamed");

        var sentMetadata = message.RootElement.GetProperty("params").GetProperty("metadata");
        sentMetadata.GetProperty("resourceKey").GetString().Should().Be("project:site/renamed.html");
        sentMetadata.GetProperty("fileName").GetString().Should().Be("renamed.html");
    }
}
