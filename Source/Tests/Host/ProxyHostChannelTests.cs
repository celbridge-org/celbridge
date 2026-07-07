using Celbridge.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Celbridge.Tests.Host;

/// <summary>
/// Tests for ProxyHostChannel buffering and rebind behaviour, exercised through the broker as a view
/// and the WebSocket endpoint would.
/// </summary>
[TestFixture]
public class ProxyHostChannelTests
{
    private IServiceProvider? _previousServiceProvider;

    [SetUp]
    public void SetUp()
    {
        // ProxyHostChannel acquires its logger through the global ServiceLocator, so initialize it with
        // the Celbridge logger registration. The previous provider is restored in TearDown.
        _previousServiceProvider = ServiceLocator.ServiceProvider;

        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Celbridge.Logging.Services.Logger<>)));
        ServiceLocator.Initialize(services.BuildServiceProvider());
    }

    [TearDown]
    public void TearDown()
    {
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
    public void BoundChannel_ForwardsOutboundMessages()
    {
        var broker = new HostChannelBroker();
        var pending = broker.CreatePendingConnection();
        IHostChannel proxy = pending.Channel;

        var socket = new MockHostChannel();
        broker.TryBindConnection(pending.Token, socket).Should().BeTrue();

        proxy.PostMessage("a");

        socket.SentMessages.Should().ContainSingle().Which.Should().Be("a");
    }

    [Test]
    public void DroppedTransport_BuffersOutbound_ThenFlushesOnReconnect()
    {
        var broker = new HostChannelBroker();
        var pending = broker.CreatePendingConnection();
        IHostChannel proxy = pending.Channel;

        var firstSocket = new MockHostChannel();
        broker.TryBindConnection(pending.Token, firstSocket).Should().BeTrue();

        // The transport drops (an OS suspend closed the socket).
        firstSocket.SimulateClosed();

        // A host->editor message sent during the outage must be buffered, not forwarded to the dead
        // socket where it would be lost. This is the reload notification in the real failure.
        proxy.PostMessage("during-outage");
        firstSocket.SentMessages.Should().BeEmpty();

        // The page reconnects on the same token; the broker re-binds a fresh socket to the same proxy.
        var secondSocket = new MockHostChannel();
        broker.TryBindConnection(pending.Token, secondSocket).Should().BeTrue();

        secondSocket.SentMessages.Should().ContainSingle().Which.Should().Be("during-outage");
    }

    [Test]
    public void StaleTransportClose_AfterRebind_DoesNotDiscardTheNewBinding()
    {
        var broker = new HostChannelBroker();
        var pending = broker.CreatePendingConnection();
        IHostChannel proxy = pending.Channel;

        var firstSocket = new MockHostChannel();
        broker.TryBindConnection(pending.Token, firstSocket).Should().BeTrue();

        var secondSocket = new MockHostChannel();
        broker.TryBindConnection(pending.Token, secondSocket).Should().BeTrue();

        // A late Closed from the replaced socket must not unbind the live one.
        firstSocket.SimulateClosed();

        proxy.PostMessage("after-rebind");
        secondSocket.SentMessages.Should().ContainSingle().Which.Should().Be("after-rebind");
    }

    [Test]
    public void Rebound_IsRaised_OnlyWhenAFreshTransportReplacesAnEarlierOne()
    {
        var broker = new HostChannelBroker();
        var pending = broker.CreatePendingConnection();
        var proxy = pending.Channel;

        var reboundCount = 0;
        proxy.Rebound += (_, _) => reboundCount++;

        // The first bind is the page's initial connection, not a reconnect.
        var firstSocket = new MockHostChannel();
        broker.TryBindConnection(pending.Token, firstSocket).Should().BeTrue();
        reboundCount.Should().Be(0);

        firstSocket.SimulateClosed();

        var secondSocket = new MockHostChannel();
        broker.TryBindConnection(pending.Token, secondSocket).Should().BeTrue();
        reboundCount.Should().Be(1);
    }

    [Test]
    public void PendingOutboundBuffer_DropsOldestMessages_WhenFull()
    {
        var broker = new HostChannelBroker();
        var pending = broker.CreatePendingConnection();
        IHostChannel proxy = pending.Channel;

        // One message over the cap of 1000: the oldest is dropped, the rest are kept in order.
        for (var index = 0; index <= 1000; index++)
        {
            proxy.PostMessage($"message-{index}");
        }

        var socket = new MockHostChannel();
        broker.TryBindConnection(pending.Token, socket).Should().BeTrue();

        socket.SentMessages.Should().HaveCount(1000);
        socket.SentMessages[0].Should().Be("message-1");
        socket.SentMessages[^1].Should().Be("message-1000");
    }

    [Test]
    public void InboundMessages_FromReconnectedTransport_AreReRaised()
    {
        var broker = new HostChannelBroker();
        var pending = broker.CreatePendingConnection();
        IHostChannel proxy = pending.Channel;

        var received = new List<string>();
        proxy.MessageReceived += (_, json) => received.Add(json);

        var firstSocket = new MockHostChannel();
        broker.TryBindConnection(pending.Token, firstSocket).Should().BeTrue();
        firstSocket.SimulateClosed();

        var secondSocket = new MockHostChannel();
        broker.TryBindConnection(pending.Token, secondSocket).Should().BeTrue();

        secondSocket.SimulateMessage("hello");

        received.Should().ContainSingle().Which.Should().Be("hello");
    }
}
