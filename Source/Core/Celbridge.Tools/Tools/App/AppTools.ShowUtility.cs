using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Show a utility by id: reveal it where it is, or move it to a workspace area first.</summary>
    /// <param name="utilityId">The utility to show: a built-in id ("celbridge.explorer", "celbridge.search", "celbridge.project-settings", "celbridge.workshop") or a custom id in "{packageName}.{contributionId}" form.</param>
    /// <param name="area">Workspace area to move the utility to before revealing it: "utility" (the Utility Panel rail), or "main", "bottom" or "side" (a document tab in that area). "document" is accepted as an alias for the area the utility declares as its dock area. Pass an empty string to reveal the utility wherever it currently is without moving it. Only a custom utility moves between areas: naming an area that a built-in or a launcher is not already in fails rather than being ignored.</param>
    [McpServerTool(Name = "app_show_utility")]
    [ToolAlias("app.show_utility")]
    [RelatedGuides("workspace_panels")]
    public async partial Task<CallToolResult> ShowUtility(string utilityId, string area)
    {
        if (!EditorId.TryParse(utilityId, out var parsedUtilityId))
        {
            return ToolResponse.Error(Result.Fail("A valid utilityId is required: lowercase letters, digits, dots, and hyphens."));
        }

        WorkspaceArea? targetArea = null;
        if (!string.IsNullOrEmpty(area))
        {
            var areaResult = await ResolveUtilityAreaAsync(parsedUtilityId, area);
            if (areaResult.IsFailure)
            {
                return ToolResponse.Error(areaResult);
            }
            targetArea = areaResult.Value;
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

    // Accepted alongside the area tokens as "this utility's own dock area", so an agent can move a utility
    // into a tab without first looking up which area it declares.
    private const string DocumentAreaAlias = "document";

    // Resolves the area token an agent passed. The alias is answered from the utility's declaration, which is
    // why this asks for the utilities state rather than parsing the token alone.
    private async Task<Result<WorkspaceArea>> ResolveUtilityAreaAsync(EditorId utilityId, string token)
    {
        if (token != DocumentAreaAlias)
        {
            if (!WorkspaceAreaTokens.TryParse(token, out var namedArea))
            {
                return Result<WorkspaceArea>.Fail(
                    $"Invalid area '{token}'. Valid values are '{WorkspaceAreaTokens.Utility}', " +
                    $"'{WorkspaceAreaTokens.Main}', '{WorkspaceAreaTokens.Bottom}', " +
                    $"'{WorkspaceAreaTokens.Side}' and '{DocumentAreaAlias}'.");
            }

            return namedArea;
        }

        var stateResult = await ExecuteCommandAsync<IGetUtilitiesStateCommand, UtilitiesStateSnapshot>();
        if (stateResult.IsFailure)
        {
            return Result<WorkspaceArea>.Fail($"Failed to resolve the dock area for utility '{utilityId}'")
                .WithErrors(stateResult);
        }

        var utility = stateResult.Value.Utilities.FirstOrDefault(entry => entry.UtilityId == utilityId);
        if (utility is null)
        {
            return Result<WorkspaceArea>.Fail($"No utility found with id '{utilityId}'");
        }

        if (utility.DockArea is null)
        {
            return Result<WorkspaceArea>.Fail(
                $"Utility '{utilityId}' stays in the Utility Panel and has no dock area. " +
                $"Name an area instead, or omit it to reveal the utility where it is.");
        }

        return utility.DockArea.Value;
    }
}
