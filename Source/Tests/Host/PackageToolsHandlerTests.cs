using System.Reflection;
using System.Text.Json;
using Celbridge.Host;
using Celbridge.Server;
using StreamJsonRpc;

namespace Celbridge.Tests.Host;

[TestFixture]
public class PackageToolsHandlerTests
{
    [Test]
    public async Task ListToolsAsync_ReturnsEveryToolTheBridgeOffers()
    {
        var bridge = new StubToolBridge
        {
            Tools = new[]
            {
                Descriptor("app_get_state", "app.get_state"),
                Descriptor("document_open",   "document.open"),
                Descriptor("file_read",       "file.read")
            }
        };
        var handler = new PackageToolsHandler(bridge);

        var result = await handler.ListToolsAsync();

        result.Select(t => t.Alias).Should().BeEquivalentTo("app.get_state", "document.open", "file.read");
    }

    [Test]
    public async Task ListToolsAsync_HidesWebViewNamespace()
    {
        var bridge = new StubToolBridge
        {
            Tools = new[]
            {
                Descriptor("app_get_state", "app.get_state"),
                Descriptor("webview_eval",    "webview.eval"),
                Descriptor("webview_reload",  "webview.reload")
            }
        };
        var handler = new PackageToolsHandler(bridge);

        var result = await handler.ListToolsAsync();

        result.Select(t => t.Alias).Should().BeEquivalentTo("app.get_state");
    }

    [Test]
    public async Task ListToolsAsync_HidesWorkshopTools()
    {
        var bridge = new StubToolBridge
        {
            Tools = new[]
            {
                Descriptor("app_list_packages", "app.list_packages"),
                Descriptor("explorer_archive",  "explorer.archive"),
                Descriptor("package_install",   "package.install"),
                Descriptor("page_publish",      "page.publish")
            }
        };
        var handler = new PackageToolsHandler(bridge);

        var result = await handler.ListToolsAsync();

        result.Select(t => t.Alias).Should().BeEquivalentTo("app.list_packages", "explorer.archive");
    }

    // Every package_* and page_* tool reaches the workshop, so a package_* tool added later fails here until
    // the handler withholds it.
    [Test]
    public async Task ListToolsAsync_WithholdsEveryPackageAndPageTool()
    {
        var packageAndPageTools = DiscoverTools()
            .Where(tool => tool.Name.StartsWith("package_", StringComparison.Ordinal)
                || tool.Name.StartsWith("page_", StringComparison.Ordinal))
            .ToArray();
        packageAndPageTools.Should().NotBeEmpty("the package_* and page_* tools should be discoverable by reflection");

        var bridge = new StubToolBridge { Tools = packageAndPageTools };
        var handler = new PackageToolsHandler(bridge);

        var result = await handler.ListToolsAsync();

        result.Should().BeEmpty();
    }

    [Test]
    public void CallToolAsync_WebViewNamespace_ThrowsDenied()
    {
        var bridge = new StubToolBridge();
        var handler = new PackageToolsHandler(bridge);

        Func<Task> act = () => handler.CallToolAsync("webview.eval", (JsonElement?)null);

        act.Should()
            .ThrowAsync<LocalRpcException>()
            .Result
            .Which
            .ErrorCode.Should().Be(ToolRpcErrorCodes.ToolDenied);

        bridge.LastCallName.Should().BeNull();
    }

    [Test]
    public void CallToolAsync_WebViewMcpStyleName_ThrowsDenied()
    {
        var bridge = new StubToolBridge();
        var handler = new PackageToolsHandler(bridge);

        Func<Task> act = () => handler.CallToolAsync("webview_eval", (JsonElement?)null);

        act.Should()
            .ThrowAsync<LocalRpcException>()
            .Result
            .Which
            .ErrorCode.Should().Be(ToolRpcErrorCodes.ToolDenied);
    }

    [TestCase("package.publish")]
    [TestCase("package_set_alias")]
    [TestCase("page.unpublish")]
    [TestCase("page_list")]
    public void CallToolAsync_WorkshopTool_ThrowsDenied(string name)
    {
        var bridge = new StubToolBridge();
        var handler = new PackageToolsHandler(bridge);

        Func<Task> act = () => handler.CallToolAsync(name, (JsonElement?)null);

        act.Should()
            .ThrowAsync<LocalRpcException>()
            .Result
            .Which
            .ErrorCode.Should().Be(ToolRpcErrorCodes.ToolDenied);

        bridge.LastCallName.Should().BeNull();
    }

    [TestCase("app.list_packages")]
    [TestCase("explorer.archive")]
    [TestCase("explorer.unarchive")]
    public async Task CallToolAsync_PackageListingAndArchiveTools_ReachTheBridge(string name)
    {
        var bridge = new StubToolBridge();
        var handler = new PackageToolsHandler(bridge);

        await handler.CallToolAsync(name, (JsonElement?)null);

        bridge.LastCallName.Should().Be(name);
    }

    [Test]
    public async Task CallToolAsync_ReturnsBridgeResult()
    {
        var bridge = new StubToolBridge
        {
            CallResult = new ToolCallResult(true, string.Empty, "0.2.5")
        };
        var handler = new PackageToolsHandler(bridge);

        var result = await handler.CallToolAsync("app.get_state", (JsonElement?)null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("0.2.5");
        bridge.LastCallName.Should().Be("app.get_state");
    }

    [Test]
    public void CallToolAsync_EmptyName_ThrowsInvalidArgs()
    {
        var bridge = new StubToolBridge();
        var handler = new PackageToolsHandler(bridge);

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
        var handler = new PackageToolsHandler(bridge);

        Func<Task> act = () => handler.CallToolAsync("app.get_state", (JsonElement?)null);

        act.Should()
            .ThrowAsync<LocalRpcException>()
            .Result
            .Which
            .ErrorCode.Should().Be(ToolRpcErrorCodes.ToolFailed);
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

    // Every MCP tool in the tool assembly, described by its MCP name and alias.
    private static IReadOnlyList<ToolDescriptor> DiscoverTools()
    {
        var tools = new List<ToolDescriptor>();

        foreach (var type in typeof(Celbridge.Tools.AppTools).Assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var toolName = method.GetCustomAttribute<ModelContextProtocol.Server.McpServerToolAttribute>()?.Name;
                if (string.IsNullOrEmpty(toolName))
                {
                    continue;
                }

                var alias = method.GetCustomAttribute<Celbridge.Tools.ToolAliasAttribute>()?.Alias ?? string.Empty;
                tools.Add(Descriptor(toolName, alias));
            }
        }

        return tools
            .DistinctBy(tool => tool.Name)
            .ToList();
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

        public Task<string> GetRawToolsListJsonAsync()
        {
            return Task.FromResult(string.Empty);
        }
    }
}
