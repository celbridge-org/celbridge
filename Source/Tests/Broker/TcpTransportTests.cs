using System.Net;
using System.Net.Sockets;
using Celbridge.Broker;
using Celbridge.Broker.Services;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Celbridge.Tests;

[TestFixture]
public class TcpTransportTests
{
    private TcpTransport? _transport;
    private BrokerRpcHandler? _brokerRpcHandler;
    private CancellationTokenSource? _cancellationTokenSource;
    private int _port;

    [SetUp]
    public void Setup()
    {
        // Create a mock IApplicationServiceProvider for tool classes
        var mockServiceProvider = Substitute.For<IApplicationServiceProvider>();

        // Build the RPC handler and transport
        var rpcHandlerLogger = Substitute.For<ILogger<BrokerRpcHandler>>();
        var transportLogger = Substitute.For<ILogger<TcpTransport>>();
        _brokerRpcHandler = new BrokerRpcHandler(mockServiceProvider, rpcHandlerLogger);
        _transport = new TcpTransport(transportLogger, _brokerRpcHandler);

        // Find a free port and start listening
        _port = GetAvailableTcpPort();
        _cancellationTokenSource = new CancellationTokenSource();
        _ = Task.Run(() => _transport.StartListeningAsync(_port, _cancellationTokenSource.Token));

        // Brief pause to let the listener start
        Thread.Sleep(100);
    }

    [TearDown]
    public void TearDown()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _transport?.Dispose();
    }

    [Test]
    public async Task ToolsList_ReturnsDiscoveredTools()
    {
        using var client = await ConnectClientAsync();

        var result = await client.InvokeWithParameterObjectAsync<JArray>("tools/list");

        result.Should().NotBeNull();
        result!.Count.Should().BeGreaterThan(0);

        // Verify a known tool is present
        var versionTool = result.FirstOrDefault(
            t => t["name"]?.Value<string>() == "app_version");
        versionTool.Should().NotBeNull();

        // Verify alias is included
        versionTool!["alias"]!.Value<string>().Should().Be("app_version");
    }

    [Test]
    public async Task ToolsList_IncludesParameterMetadata()
    {
        using var client = await ConnectClientAsync();

        var result = await client.InvokeWithParameterObjectAsync<JArray>("tools/list");

        // Find a tool with parameters
        var openTool = result!.FirstOrDefault(
            t => t["name"]?.Value<string>() == "document_open");
        openTool.Should().NotBeNull();

        var parameters = openTool!["parameters"] as JArray;
        parameters.Should().NotBeNull();
        parameters!.Count.Should().Be(2);
        parameters[0]["name"]!.Value<string>().Should().Be("fileResource");
        parameters[0]["type"]!.Value<string>().Should().Be("str");
    }

    [Test]
    public async Task ToolsCall_UnknownTool_ReturnsFailure()
    {
        using var client = await ConnectClientAsync();

        var result = await client.InvokeWithParameterObjectAsync<JObject>(
            "tools/call",
            new
            {
                name = "does/not/exist",
                arguments = new { }
            });

        result.Should().NotBeNull();
        result!["isSuccess"]!.Value<bool>().Should().BeFalse();
        result["errorMessage"]!.Value<string>().Should().Contain("Unknown tool");
    }

    [Test]
    public async Task ConnectionEvents_FiredOnConnectAndDisconnect()
    {
        Guard.IsNotNull(_transport);

        var connectedId = -1;
        var disconnectedId = -1;
        var connectedEvent = new TaskCompletionSource();
        var disconnectedEvent = new TaskCompletionSource();

        _transport.ConnectionAccepted += (id) =>
        {
            connectedId = id;
            connectedEvent.TrySetResult();
        };
        _transport.ConnectionLost += (id) =>
        {
            disconnectedId = id;
            disconnectedEvent.TrySetResult();
        };

        var client = await ConnectClientAsync();

        // Wait for connection event
        await connectedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        connectedId.Should().BeGreaterThan(0);
        _transport.ActiveConnectionCount.Should().Be(1);

        // Disconnect
        client.Dispose();

        // Wait for disconnection event
        await disconnectedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        disconnectedId.Should().Be(connectedId);
        _transport.ActiveConnectionCount.Should().Be(0);
    }

    [Test]
    public async Task AdditionalRpcTarget_MethodsAreAccessible()
    {
        Guard.IsNotNull(_transport);
        Guard.IsNotNull(_brokerRpcHandler);

        // Register an additional RPC target before any connections
        var additionalHandler = new TestAdditionalHandler();
        _transport.AddRpcTarget(additionalHandler);

        // Need to restart with the new target - dispose and recreate
        _cancellationTokenSource?.Cancel();
        _transport.Dispose();

        var transportLogger = Substitute.For<ILogger<TcpTransport>>();
        _transport = new TcpTransport(transportLogger, _brokerRpcHandler);
        _transport.AddRpcTarget(additionalHandler);

        _port = GetAvailableTcpPort();
        _cancellationTokenSource = new CancellationTokenSource();
        _ = Task.Run(() => _transport.StartListeningAsync(_port, _cancellationTokenSource.Token));
        Thread.Sleep(100);

        using var client = await ConnectClientAsync();

        var result = await client.InvokeAsync<string>("Ping");

        result.Should().Be("pong from additional handler");
    }

    private async Task<JsonRpc> ConnectClientAsync()
    {
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, _port);
        var stream = tcpClient.GetStream();
        var jsonRpc = new JsonRpc(stream, stream);
        jsonRpc.StartListening();
        return jsonRpc;
    }

    private static int GetAvailableTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>
/// A test handler to verify additional RPC targets work on the transport.
/// </summary>
public class TestAdditionalHandler
{
    public string Ping()
    {
        return "pong from additional handler";
    }
}
