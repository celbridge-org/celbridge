using System.Text.Json;
using Celbridge.Server;
using StreamJsonRpc;

namespace Celbridge.Host;

public static class ToolRpcMethods
{
    public const string ListTools = "tools/list";
    public const string CallTool = "tools/call";
}

/// <summary>
/// JSON-RPC error codes for tools/call failures.
/// </summary>
public static class ToolRpcErrorCodes
{
    public const int ToolNotFound = -32001;
    public const int ToolDenied = -32002;
    public const int ToolInvalidArgs = -32003;
    public const int ToolFailed = -32004;
}

/// <summary>
/// Per-WebView RPC target for tools/list and tools/call, gated by a package's
/// requires_tools allowlist.
/// </summary>
public sealed class ContributionToolsHandler
{
    private readonly IMcpToolBridge _bridge;
    private readonly IReadOnlyList<string> _allowedPatterns;

    public ContributionToolsHandler(IMcpToolBridge bridge, IReadOnlyList<string> allowedPatterns)
    {
        _bridge = bridge;
        _allowedPatterns = allowedPatterns;
    }

    public IReadOnlyList<string> AllowedPatterns => _allowedPatterns;

    [JsonRpcMethod(ToolRpcMethods.ListTools)]
    public async Task<IReadOnlyList<ToolDescriptor>> ListToolsAsync()
    {
        var allTools = await _bridge.ListToolsAsync();
        var filtered = new List<ToolDescriptor>(allTools.Count);

        foreach (var tool in allTools)
        {
            if (ToolAllowlist.IsAllowed(tool.Alias, _allowedPatterns))
            {
                filtered.Add(tool);
            }
        }

        return filtered;
    }

    [JsonRpcMethod(ToolRpcMethods.CallTool)]
    public async Task<ToolCallResult> CallToolAsync(string name, JsonElement? arguments)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new LocalRpcException("Tool name must not be empty")
            {
                ErrorCode = ToolRpcErrorCodes.ToolInvalidArgs
            };
        }

        if (!ToolAllowlist.IsAllowed(name, _allowedPatterns))
        {
            throw new LocalRpcException($"Tool '{name}' is not in this package's requires_tools allowlist")
            {
                ErrorCode = ToolRpcErrorCodes.ToolDenied
            };
        }

        try
        {
            return await _bridge.CallToolAsync(name, arguments);
        }
        catch (LocalRpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LocalRpcException($"Tool '{name}' failed: {ex.Message}")
            {
                ErrorCode = ToolRpcErrorCodes.ToolFailed
            };
        }
    }
}
