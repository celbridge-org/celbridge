using System.Threading.Channels;
using Celbridge.LanguageServers;
using Celbridge.LanguageServers.Services;
using Celbridge.Tests.Helpers;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers the language channel of a code editor view.
/// </summary>
[TestFixture]
public class LanguageChannelTests
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private ILanguageServerService _languageServerService = null!;
    private FakeChannelHost _host = null!;
    private List<FakePageConnection> _connections = null!;
    private LanguageChannel _channel = null!;

    [SetUp]
    public void Setup()
    {
        _connections = new List<FakePageConnection>();

        _languageServerService = Substitute.For<ILanguageServerService>();
        _languageServerService.ConnectPage(Arg.Any<ResourceKey>()).Returns(callInfo =>
        {
            var connection = new FakePageConnection(callInfo.Arg<ResourceKey>());
            _connections.Add(connection);
            return connection;
        });

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.HasWorkspaceService.Returns(true);
        workspaceWrapper.WorkspaceService.LanguageServerService.Returns(_languageServerService);

        _host = new FakeChannelHost
        {
            Resource = new ResourceKey("Scripts/main.ts")
        };

        _channel = new LanguageChannel(workspaceWrapper, new NullLogger<LanguageChannel>());
        _channel.RegisterTargets(_host);
    }

    [TearDown]
    public void TearDown()
    {
        _channel.Dispose();
    }

    [Test]
    public void RegisterTargets_RegistersTheChannelsRpcMethods()
    {
        _host.RpcTarget.Should().BeSameAs(_channel);
    }

    [Test]
    public void Open_ConnectsThePageOfTheHostsCurrentResource()
    {
        _channel.OnOpen();
        _host.Resource = new ResourceKey("Scripts/renamed.ts");
        _channel.OnOpen();

        _connections.Should().HaveCount(2);
        _connections[0].Resource.Should().Be(new ResourceKey("Scripts/main.ts"));
        _connections[1].Resource.Should().Be(new ResourceKey("Scripts/renamed.ts"));
    }

    [Test]
    public async Task Open_PassesTheConnectionsMessagesToThePageInOrder()
    {
        _channel.OnOpen();

        _connections[0].SendToPage("first");
        _connections[0].SendToPage("second");
        var first = await _host.ReadNotificationAsync(ReadTimeout);
        var second = await _host.ReadNotificationAsync(ReadTimeout);

        first.Method.Should().Be("language/message");
        first.Message.Should().Be("first");
        second.Message.Should().Be("second");
    }

    [Test]
    public void Send_PassesThePagesMessageToTheOpenConnection()
    {
        _channel.OnOpen();

        _channel.OnSend("""{"id":1,"method":"textDocument/hover"}""");

        _connections[0].Received.Should().Equal("""{"id":1,"method":"textDocument/hover"}""");
    }

    [Test]
    public void Send_WithNoOpenConnection_DoesNothing()
    {
        _channel.OnSend("{}");

        _connections.Should().BeEmpty();
    }

    [Test]
    public async Task AConnectionThatEndsByItself_TellsThePageThatTheConnectionClosed()
    {
        _channel.OnOpen();

        _connections[0].SendToPage("last");
        _connections[0].End();
        var message = await _host.ReadNotificationAsync(ReadTimeout);
        var closed = await _host.ReadNotificationAsync(ReadTimeout);

        message.Message.Should().Be("last");
        closed.Method.Should().Be("language/closed");
    }

    [Test]
    public async Task Close_DisposesTheConnectionAndTellsThePageNothing()
    {
        _channel.OnOpen();

        _channel.OnClose();
        _channel.OnSend("{}");

        // The second open makes a new connection, and the next notification comes from it.
        _channel.OnOpen();
        _connections[1].SendToPage("from the second connection");
        var next = await _host.ReadNotificationAsync(ReadTimeout);

        _connections[0].WasDisposed.Should().BeTrue();
        _connections[0].Received.Should().BeEmpty();
        next.Message.Should().Be("from the second connection");
    }

    [Test]
    public void Open_WithAConnectionAlreadyOpen_DisposesThatConnectionFirst()
    {
        _channel.OnOpen();

        _channel.OnOpen();

        _connections[0].WasDisposed.Should().BeTrue();
        _connections[1].WasDisposed.Should().BeFalse();
    }

    [Test]
    public void Dispose_DisposesTheOpenConnection()
    {
        _channel.OnOpen();

        _channel.Dispose();
        _channel.OnOpen();

        _connections.Should().HaveCount(2);
        _connections[0].WasDisposed.Should().BeTrue();
        _connections[1].WasDisposed.Should().BeTrue();
    }

    private sealed record Notification(string Method, string Message);

    // A custom editor's channel host that records the notifications sent to the page.
    private sealed class FakeChannelHost : ICustomEditorChannelHost
    {
        private readonly Channel<Notification> _notifications = Channel.CreateUnbounded<Notification>();

        public ResourceKey Resource { get; set; }

        public object? RpcTarget { get; private set; }

        public void AddLocalRpcTarget<T>(T target) where T : class
        {
            RpcTarget = target;
        }

        public Task NotifyAsync(string method, object? argument)
        {
            // The channel sends a message as the "message" property of an anonymous object.
            var message = argument?.GetType().GetProperty("message")?.GetValue(argument) as string;
            _notifications.Writer.TryWrite(new Notification(method, message ?? string.Empty));

            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(string method)
        {
            throw new NotSupportedException();
        }

        public async Task<Notification> ReadNotificationAsync(TimeSpan timeout)
        {
            using var timeoutSource = new CancellationTokenSource(timeout);

            return await _notifications.Reader.ReadAsync(timeoutSource.Token);
        }
    }

    // A page's connection to a language server, driven by the test.
    private sealed class FakePageConnection : ILanguagePageConnection
    {
        private readonly Channel<string> _toPage = Channel.CreateUnbounded<string>();

        public FakePageConnection(ResourceKey resource)
        {
            Resource = resource;
        }

        public ResourceKey Resource { get; }

        public List<string> Received { get; } = new();

        public bool WasDisposed { get; private set; }

        public void SendToPage(string message)
        {
            _toPage.Writer.TryWrite(message);
        }

        public void End()
        {
            _toPage.Writer.TryComplete();
        }

        public void Receive(string message)
        {
            Received.Add(message);
        }

        public async ValueTask<string?> ReadNextAsync(CancellationToken cancellationToken)
        {
            if (!await _toPage.Reader.WaitToReadAsync(cancellationToken))
            {
                return null;
            }

            _toPage.Reader.TryRead(out var message);

            return message;
        }

        public void Dispose()
        {
            WasDisposed = true;
            End();
        }
    }
}
