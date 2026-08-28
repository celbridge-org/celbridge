using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Show a utility by id: reveal it where it is, or move it to a workspace area first.</summary>
    /// <param name="utilityId">The utility to show: a built-in id ("celbridge.explorer", "celbridge.search") or a custom id in "{packageName}.{contributionId}" form.</param>
    /// <param name="area">Optional workspace area to move the utility to before revealing it: "utility" (the Utility Panel rail), or "main", "bottom" or "side" (a document tab in that area). "document" is accepted as an alias for the utility's document area. Omit to reveal the utility wherever it currently is. Ignored for the built-in utilities, which are always in the panel.</param>
    [McpServerTool(Name = "app_show_utility")]
    [ToolAlias("app.show_utility")]
    [RelatedGuides("workspace_panels")]
    public async partial Task<CallToolResult> ShowUtility(string utilityId, string? area = null)
    {
        if (!EditorId.TryParse(utilityId, out var parsedUtilityId))
        {
            return ToolResponse.Error(Result.Fail("A valid utilityId is required: lowercase letters, digits, dots, and hyphens."));
        }

        WorkspaceArea? targetArea = null;
        if (!string.IsNullOrEmpty(area))
        {
            if (!TryParseUtilityArea(area, out var parsedArea))
            {
                return ToolResponse.Error(Result.Fail($"Invalid area '{area}'. Valid values are '{WorkspaceAreaTokens.Utility}', '{WorkspaceAreaTokens.Main}', '{WorkspaceAreaTokens.Bottom}', '{WorkspaceAreaTokens.Side}' and '{DocumentAreaAlias}'."));
            }
            targetArea = parsedArea;
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

    // Accepted alongside the area tokens as "the utility's default document area", so an agent can move a
    // utility into a tab without naming one. Mapped to Main here only while Main is the sole document area
    // a utility can occupy; once areas are declarable, resolution moves to where the declaration is known.
    private const string DocumentAreaAlias = "document";

    private static bool TryParseUtilityArea(string token, out WorkspaceArea area)
    {
        if (token == DocumentAreaAlias)
        {
            area = WorkspaceArea.Main;
            return true;
        }

        return WorkspaceAreaTokens.TryParse(token, out area);
    }
}
