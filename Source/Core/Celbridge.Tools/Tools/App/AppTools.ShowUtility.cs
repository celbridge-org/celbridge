using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Show a utility by id: reveal it where it is, or move it to a workspace area first.</summary>
    /// <param name="utilityId">The utility to show: a built-in id ("celbridge.explorer", "celbridge.search", "celbridge.project-settings", "celbridge.workshop") or a custom id in "{packageName}.{contributionId}" form.</param>
    /// <param name="area">Optional workspace area to move the utility to before revealing it: "utility" (the Utility Panel rail), or "main", "bottom" or "side" (a document tab in that area). "document" is accepted as an alias for the utility's own document area. Omit to reveal the utility wherever it currently is. Ignored for the built-in utilities, which each have one place they live.</param>
    [McpServerTool(Name = "app_show_utility")]
    [ToolAlias("app.show_utility")]
    [RelatedGuides("workspace_panels")]
    public async partial Task<CallToolResult> ShowUtility(string utilityId, string? area = null)
    {
        if (!EditorId.TryParse(utilityId, out var parsedUtilityId))
        {
            return ToolResponse.Error(Result.Fail("A valid utilityId is required: lowercase letters, digits, dots, and hyphens."));
        }

        ShowUtilityArea? targetArea = null;
        if (!string.IsNullOrEmpty(area))
        {
            targetArea = ParseUtilityArea(area);
            if (targetArea is null)
            {
                return ToolResponse.Error(Result.Fail($"Invalid area '{area}'. Valid values are '{WorkspaceAreaTokens.Utility}', '{WorkspaceAreaTokens.Main}', '{WorkspaceAreaTokens.Bottom}', '{WorkspaceAreaTokens.Side}' and '{DocumentAreaAlias}'."));
            }
        }

        var showResult = await ExecuteCommandAsync<IShowUtilityCommand>(command =>
        {
            command.UtilityId = parsedUtilityId;
            command.Area = targetArea;
        });
        if (showResult.IsFailure)
        {
            return ToolResponse.Error(showResult);
        }

        return ToolResponse.Success("ok");
    }

    // Accepted alongside the area tokens as "this utility's own document area".
    private const string DocumentAreaAlias = "document";

    // Null for a token that names no area.
    private static ShowUtilityArea? ParseUtilityArea(string token)
    {
        if (token == DocumentAreaAlias)
        {
            return ShowUtilityArea.OwnDocumentArea;
        }

        if (!WorkspaceAreaTokens.TryParse(token, out var area))
        {
            return null;
        }

        return ShowUtilityArea.Named(area);
    }
}
