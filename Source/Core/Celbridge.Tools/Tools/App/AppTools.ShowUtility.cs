using Celbridge.Documents;
using Celbridge.Workspace;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Show a utility by id: reveal it where it is, or move it to a dock location first.</summary>
    /// <param name="utilityId">The utility to show: a built-in id ("celbridge.explorer", "celbridge.search") or a custom id in "{packageName}.{contributionId}" form.</param>
    /// <param name="location">Optional dock location to move the utility to before revealing it: "panel" (the Utility Panel rail) or "document" (a document tab). Omit to reveal the utility wherever it currently is. Ignored for the built-in utilities, which are always in the panel.</param>
    [McpServerTool(Name = "app_show_utility")]
    [ToolAlias("app.show_utility")]
    [RelatedGuides("workspace_panels")]
    public async partial Task<CallToolResult> ShowUtility(string utilityId, string? location = null)
    {
        if (!EditorInstanceId.TryParse(utilityId, out var parsedUtilityId))
        {
            return ToolResponse.Error(Result.Fail("A valid utilityId is required: lowercase letters, digits, dots, and hyphens."));
        }

        DockLocation? dockLocation = null;
        if (!string.IsNullOrEmpty(location))
        {
            if (!DockLocationTokens.TryParse(location, out var parsedLocation))
            {
                return ToolResponse.Error(Result.Fail($"Invalid location '{location}'. Valid values are '{DockLocationTokens.Panel}' and '{DockLocationTokens.Document}'."));
            }
            dockLocation = parsedLocation;
        }

        var showResult = await ExecuteCommandAsync<IShowUtilityCommand>(command =>
        {
            command.UtilityId = parsedUtilityId;
            command.Location = dockLocation;
        });
        if (showResult.IsFailure)
        {
            return ToolResponse.Error(showResult);
        }

        return ToolResponse.Success("ok");
    }
}
