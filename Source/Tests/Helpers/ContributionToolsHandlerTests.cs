using System.Text.Json;
using Celbridge.Host;
using Celbridge.Server;
using StreamJsonRpc;

namespace Celbridge.Tests.Helpers;

[TestFixture]
public class ContributionToolsHandlerTests
{
    [Test]
    public async Task ListToolsAsync_FiltersByAllowlist()
    {
        var bridge = new StubToolBridge
        {
            Tools = new[]
            {
                Descriptor("app_get_version", "app.get_version"),
                Descriptor("document_open",   "document.open"),
                Descriptor("file_read",       "file.read")
            }
        };
        var handler = new ContributionToolsHandler(bridge, new[] { "app.*", "document.open" });

        var result = await handler.ListToolsAsync();

        result.Select(t => t.Alias).Should().BeEquivalentTo("app.get_version", "document.open");
    }

    [Test]
    public async Task CallToolAsync_AllowedTool_ReturnsBridgeResult()
    {
        var bridge = new StubToolBridge
        {
            CallResult = new ToolCallResult(true, string.Empty, "0.2.5")
        };
        var handler = new ContributionToolsHandler(bridge, new[] { "app.*" });

        var result = await handler.CallToolAsync("app.get_version", (JsonElement?)null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("0.2.5");
        bridge.LastCallName.Should().Be("app.get_version");
    }

    [Test]
    public void CallToolAsync_DeniedTool_ThrowsLocalRpcExceptionWithDeniedCode()
    {
        var bridge = new StubToolBridge();
        var handler = new ContributionToolsHandler(bridge, new[] { "app.*" });

        Func<Task> act = () => handler.CallToolAsync("file.read", (JsonElement?)null);

        act.Should()
            .ThrowAsync<LocalRpcException>()
            .Result
            .Which
            .ErrorCode.Should().Be(ToolRpcErrorCodes.ToolDenied);

        bridge.LastCallName.Should().BeNull();
    }

    [Test]
    public void CallToolAsync_EmptyName_ThrowsInvalidArgs()
    {
        var bridge = new StubToolBridge();
        var handler = new ContributionToolsHandler(bridge, new[] { "*" });

        Func<Task> act = () => handler.CallToolAsync("", (JsonElement?)null);

        act.Should()
            .ThrowAsync<LocalRpcException>()
            .Result
            .Which
            .ErrorCode.Should().Be(ToolRpcErrorCodes.ToolInvalidArgs);
    }

    [Test]
    public void CallToolAsync_BridgeThrows_WrapsAsToolFailed()
    {
        var bridge = new StubToolBridge
        {
            CallThrows = new InvalidOperationException("boom")
        };
        var handler = new ContributionToolsHandler(bridge, new[] { "*" });

        Func<Task> act = () => handler.CallToolAsync("app.get_version", (JsonElement?)null);

        act.Should()
            .ThrowAsync<LocalRpcException>()
            .Result
            .Which
            .ErrorCode.Should().Be(ToolRpcErrorCodes.ToolFailed);
    }

    [Test]
    public void AllowedPatterns_IsSurfaced()
    {
        var handler = new ContributionToolsHandler(new StubToolBridge(), new[] { "app.*", "file.read" });
        handler.AllowedPatterns.Should().Equal("app.*", "file.read");
    }

    private static ToolDescriptor Descriptor(string name, string alias)
    {
        return new ToolDescriptor(
            Name: name,
            Alias: alias,
            Description: string.Empty,
            ReturnType: string.Empty,
            Parameters: Array.Empty<ToolParameter>());
    }

    private sealed class StubToolBridge : IMcpToolBridge
    {
        public IReadOnlyList<ToolDescriptor> Tools { get; set; } = Array.Empty<ToolDescriptor>();
        public ToolCallResult CallResult { get; set; } = new ToolCallResult(true, string.Empty, null);
        public Exception? CallThrows { get; set; }
        public string? LastCallName { get; private set; }

        public Task<IReadOnlyList<ToolDescriptor>> ListToolsAsync()
        {
            return Task.FromResult(Tools);
        }

        public Task<ToolCallResult> CallToolAsync(string name, object? arguments)
        {
            LastCallName = name;

            if (CallThrows is not null)
            {
                throw CallThrows;
            }

            return Task.FromResult(CallResult);
        }
    }
}
