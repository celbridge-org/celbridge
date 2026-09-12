using System.Reflection;
using Celbridge.Settings;
using Celbridge.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Server.Services;

/// <summary>
/// Withholds the workshop tools from every MCP client while the workshop feature flag is off. The
/// single gate for both callers: agents reach the endpoint directly, and package WebViews reach it
/// through McpToolBridge, so filtering here covers both.
/// </summary>
internal sealed class WorkshopToolFilter
{
    // Tool names carrying [WorkshopTool], resolved once from the tool assembly.
    private static readonly IReadOnlySet<string> WorkshopToolNames = ReadWorkshopToolNames();

    private readonly IFeatureFlags _featureFlags;

    public WorkshopToolFilter(IFeatureFlags featureFlags)
    {
        _featureFlags = featureFlags;
    }

    public static IReadOnlySet<string> ToolNames => WorkshopToolNames;

    /// <summary>
    /// Returns true if the named tool reaches the workshop and this build did not opt in. The whole
    /// decision both filters make, separated so it can be exercised without an McpServer.
    /// </summary>
    public bool ShouldWithhold(string? toolName)
    {
        return !string.IsNullOrEmpty(toolName)
            && WorkshopToolNames.Contains(toolName)
            && !_featureFlags.IsEnabled(FeatureFlagConstants.Workshop);
    }

    private bool IsEnabled => _featureFlags.IsEnabled(FeatureFlagConstants.Workshop);

    /// <summary>
    /// Drops the workshop tools from tools/list, so a build without the feature never offers them.
    /// </summary>
    public McpRequestFilter<ListToolsRequestParams, ListToolsResult> CreateListFilter()
    {
        return next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);

            if (IsEnabled || result.Tools.Count == 0)
            {
                return result;
            }

            result.Tools = result.Tools
                .Where(tool => !WorkshopToolNames.Contains(tool.Name))
                .ToList();

            return result;
        };
    }

    /// <summary>
    /// Refuses a workshop tool call. Needed alongside the list filter because the tools stay
    /// registered, so a client that names one directly would otherwise reach it.
    /// </summary>
    public McpRequestFilter<CallToolRequestParams, CallToolResult> CreateCallFilter()
    {
        return next => async (context, cancellationToken) =>
        {
            if (!ShouldWithhold(context.Params?.Name))
            {
                return await next(context, cancellationToken);
            }

            return ToolResponse.FeatureFlagDisabled(FeatureFlagConstants.Workshop);
        };
    }

    private static IReadOnlySet<string> ReadWorkshopToolNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in typeof(AppTools).Assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<WorkshopToolAttribute>() is null)
                {
                    continue;
                }

                var toolName = method.GetCustomAttribute<McpServerToolAttribute>()?.Name;
                if (!string.IsNullOrEmpty(toolName))
                {
                    names.Add(toolName);
                }
            }
        }

        return names;
    }
}
