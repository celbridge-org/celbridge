using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Highlight a named UI landmark with a teaching-tip callout to orient the user; an empty target clears it.</summary>
    [McpServerTool(Name = "app_spotlight")]
    [ToolAlias("app.spotlight")]
    [RelatedGuides]
    public async partial Task<CallToolResult> Spotlight(string target, string label = "", int durationMs = 0)
    {
        // An empty target is the clear sentinel and is handled before catalog
        // validation, so clearing never trips the unknown-target troubleshooter.
        if (!string.IsNullOrEmpty(target) &&
            !LandmarkCatalog.All.Any(landmark => landmark.Id == target))
        {
            var validTargets = string.Join(", ", LandmarkCatalog.All.Select(landmark => landmark.Id));
            return ToolResponse.SpotlightTargetNotFound(target, validTargets);
        }

        var spotlightResult = await ExecuteCommandAsync<ISpotlightCommand>(command =>
        {
            command.Target = target;
            command.Label = label;
            command.DurationMs = durationMs;
        });
        if (spotlightResult.IsFailure)
        {
            return ToolResponse.Error(spotlightResult);
        }

        return ToolResponse.Success("ok");
    }
}
