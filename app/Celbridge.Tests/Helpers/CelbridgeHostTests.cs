using Celbridge.Host;

namespace Celbridge.Tests.Helpers;

/// <summary>
/// Tests for CelbridgeHost facade behavior.
/// </summary>
[TestFixture]
public class CelbridgeHostTests
{
    private MockHostChannel _channel = null!;
    private CelbridgeHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _channel = new MockHostChannel();
        _host = new CelbridgeHost(_channel);
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();
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
        await _host.NotifyExternalChangeAsync();

        // Assert
        _channel.SentMessages.Should().HaveCount(1);
        _channel.SentMessages[0].Should().Contain("document/externalChange");
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
