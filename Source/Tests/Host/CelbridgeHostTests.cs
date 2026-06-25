using Celbridge.Host;
using Celbridge.Resources;
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
        _host = new CelbridgeHost(_channel);
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();

        if (_previousServiceProvider is not null)
        {
            ServiceLocator.Initialize(_previousServiceProvider);
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
    public async Task NotifyLocalizationUpdatedAsync_SendsCorrectMethodWithStrings()
    {
        // Arrange
        _host.StartListening();
        var strings = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };

        // Act
        await _host.NotifyLocalizationUpdatedAsync(strings);

        // Assert
        _channel.SentMessages.Should().HaveCount(1);
        _channel.SentMessages[0].Should().Contain("localization/updated");
        _channel.SentMessages[0].Should().Contain("key1");
        _channel.SentMessages[0].Should().Contain("value1");
    }
}
