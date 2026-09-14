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
/// Per-WebView RPC target for tools/list and tools/call. This is the single chokepoint between a
/// package editor and the tool surface, and the seam a privilege model plugs into. The only tools it
/// withholds are the ones no package may reach regardless of what it declares.
/// </summary>
public sealed class PackageToolsHandler
{
    // The package tools that reach the workshop server with the user's Workshop Key. Every page tool
    // reaches the workshop, so the page namespace is withheld as a whole.
    private static readonly HashSet<string> WorkshopPackageTools = new(StringComparer.Ordinal)
    {
        "package_list",
        "package_info",
        "package_install",
        "package_publish",
        "package_set_alias",
        "package_remove_alias",
        "package_delete",
        "package_unpublish",
    };

    private readonly IMcpToolBridge _bridge;

    public PackageToolsHandler(IMcpToolBridge bridge)
    {
        _bridge = bridge;
    }

    [JsonRpcMethod(ToolRpcMethods.ListTools)]
    public async Task<IReadOnlyList<ToolDescriptor>> ListToolsAsync()
    {
        var allTools = await _bridge.ListToolsAsync();
        var filtered = new List<ToolDescriptor>(allTools.Count);

        foreach (var tool in allTools)
        {
            if (IsCustomEditorRestricted(tool.Alias))
            {
                continue;
            }

            // Descriptions are prose written for an agent choosing a tool. The cel.* proxy keys off
            // the alias and the parameter list, so sending them would multiply the payload every
            // editor loads at startup for text nothing on the page reads.
            filtered.Add(tool with { Description = string.Empty });
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

        // The webview_* namespace and the workshop tools are reserved for agents and Python. Blocking
        // webview_* closes the cross-document attack vector where a custom editor's JS could call
        // webview.eval against another open document. Blocking the workshop tools keeps package code
        // from publishing, installing or moving aliases with the user's Workshop Key.
        if (IsCustomEditorRestricted(name))
        {
            throw new LocalRpcException($"Tool '{name}' is not accessible from custom editors")
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

    /// <summary>
    /// Returns true if the tool is forbidden inside custom editor WebViews: the webview_* namespace, the
    /// page_* namespace, and the package_* tools that reach the workshop. Both MCP-style names and alias
    /// dotted names are matched.
    /// </summary>
    private static bool IsCustomEditorRestricted(string name)
    {
        var mcpName = ToMcpName(name);

        return mcpName.StartsWith("webview_", StringComparison.Ordinal)
            || mcpName.StartsWith("page_", StringComparison.Ordinal)
            || WorkshopPackageTools.Contains(mcpName);
    }

    // An alias is the MCP name with its first underscore swapped for a dot, so swapping it back lets one
    // check cover both forms.
    private static string ToMcpName(string name)
    {
        var dotIndex = name.IndexOf('.');
        if (dotIndex < 0)
        {
            return name;
        }

        return $"{name[..dotIndex]}_{name[(dotIndex + 1)..]}";
    }
}
