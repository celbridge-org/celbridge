using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Celbridge.Messaging;
using Celbridge.Server;
using Celbridge.Server.Services;

namespace Celbridge.Tests.Server;

/// <summary>
/// Tests for how McpToolBridge holds its MCP session. The MCP server forgets a session
/// once it has been idle for its IdleTimeout and answers 404 for it from then on, so the
/// bridge must start a new session rather than failing every call until the next restart.
/// </summary>
[TestFixture]
public class McpToolBridgeSessionTests
{
    private StubMcpServer _server = null!;
    private McpToolBridge _bridge = null!;

    [SetUp]
    public void Setup()
    {
        var serverService = Substitute.For<IServerService>();
        serverService.Port.Returns(51359);

        _server = new StubMcpServer();
        _bridge = new McpToolBridge(
            serverService,
            Substitute.For<IMessengerService>(),
            Substitute.For<ILogger<McpToolBridge>>(),
            _server);
    }

    [Test]
    public async Task CallToolAsync_AfterSessionExpires_StartsNewSessionAndSucceeds()
    {
        var firstResult = await _bridge.CallToolAsync("app_log", null);
        firstResult.IsSuccess.Should().BeTrue();

        _server.ExpireSessions();

        var secondResult = await _bridge.CallToolAsync("app_log", null);

        secondResult.IsSuccess.Should().BeTrue();
        _server.InitializeCount.Should().Be(2);
        _server.LastToolCallSessionId.Should().Be("session-2");
    }

    [Test]
    public async Task ListToolsAsync_AfterSessionExpires_ReturnsTools()
    {
        await _bridge.ListToolsAsync();

        _server.ExpireSessions();

        var tools = await _bridge.ListToolsAsync();

        tools.Should().ContainSingle(tool => tool.Name == "app_log");
    }

    [Test]
    public async Task CallToolAsync_WhenNewSessionIsAlsoRejected_FailsAfterOneRetry()
    {
        _server.RejectAllSessions = true;

        var result = await _bridge.CallToolAsync("app_log", null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("404");
        _server.InitializeCount.Should().Be(2);
    }

    // Issues numbered session ids and answers 404 for any session it does not hold,
    // matching the MCP SDK's stateful HTTP transport.
    private sealed class StubMcpServer : HttpMessageHandler
    {
        private readonly HashSet<string> _liveSessions = new();

        public int InitializeCount { get; private set; }
        public string? LastToolCallSessionId { get; private set; }
        public bool RejectAllSessions { get; set; }

        public void ExpireSessions()
        {
            _liveSessions.Clear();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var message = JsonNode.Parse(body)!;
            var method = message["method"]!.GetValue<string>();
            var requestId = message["id"]?.GetValue<int>();

            if (method == "initialize")
            {
                InitializeCount++;
                var newSessionId = $"session-{InitializeCount}";
                _liveSessions.Add(newSessionId);

                var initializeResponse = ResultResponse(requestId, "{}");
                initializeResponse.Headers.Add("Mcp-Session-Id", newSessionId);

                return initializeResponse;
            }

            string? sessionId = null;
            if (request.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues))
            {
                sessionId = sessionValues.First();
            }

            if (sessionId is null ||
                RejectAllSessions ||
                !_liveSessions.Contains(sessionId))
            {
                return JsonResponse(HttpStatusCode.NotFound, """{"error":{"code":-32001,"message":"Session not found"},"id":"","jsonrpc":"2.0"}""");
            }

            if (method == "notifications/initialized")
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            if (method == "tools/list")
            {
                return ResultResponse(requestId, """{"tools":[{"name":"app_log","description":"","inputSchema":{}}]}""");
            }

            LastToolCallSessionId = sessionId;

            return ResultResponse(requestId, """{"content":[{"type":"text","text":"ok"}],"isError":false}""");
        }

        private static HttpResponseMessage ResultResponse(int? requestId, string resultJson)
        {
            var envelope = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = requestId,
                ["result"] = JsonNode.Parse(resultJson)
            };

            return JsonResponse(HttpStatusCode.OK, envelope.ToJsonString());
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
