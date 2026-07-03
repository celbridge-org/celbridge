using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Highlight a named UI landmark with a teaching-tip callout to orient the user; an empty target clears it.</summary>
    [McpServerTool(Name = "app_spotlight")]
    [ToolAlias("app.spotlight")]
    [RelatedGuides("workspace_panels")]
    public async partial Task<CallToolResult> Spotlight(string target, string label = "", int durationMs = 0)
    {
        // An empty target is the clear sentinel and is handled before registry
        // validation, so clearing never trips the unknown-target troubleshooter.
        var landmarkRegistry = GetRequiredService<ISpotlightRegistry>();
        if (!string.IsNullOrEmpty(target) &&
            !landmarkRegistry.TryGetLandmark(target, out _))
        {
            var validTargets = string.Join(", ", landmarkRegistry.GetLandmarks().Select(landmark => landmark.Id));
            return ToolResponse.SpotlightTargetNotFound(target, validTargets);
        }

        // Agents sometimes HTML-escape characters such as & in the text they emit,
        // which would otherwise render literally (e.g. "&amp;") in the callout. The
        // label is plain text, so decode any entities before displaying it.
        var decodedLabel = System.Net.WebUtility.HtmlDecode(label);

        var spotlightResult = await ExecuteCommandAsync<ISpotlightCommand>(command =>
        {
            command.Target = target;
            command.Label = decodedLabel;
            command.DurationMs = durationMs;
        });
        if (spotlightResult.IsFailure)
        {
            return ToolResponse.Error(spotlightResult);
        }

        return ToolResponse.Success("ok");
    }
}
