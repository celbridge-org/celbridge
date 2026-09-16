using System.Text.Json;
using Celbridge.Workshop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by workshop_unpublish_page confirming the page's served content was removed.
/// </summary>
public record class WorkshopUnpublishPageResult(string Path, bool Unpublished);

public partial class WorkshopTools
{
    /// <summary>Unpublish a page from the workshop, removing its served content.</summary>
    [McpServerTool(Name = "workshop_unpublish_page", Destructive = true)]
    [ToolAlias("workshop.unpublish_page")]
    [RelatedGuides("pages_overview", "silent_vs_interactive")]
    public async partial Task<CallToolResult> UnpublishPage(string path, bool confirmWithUser = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResponse.Error("A page path is required, for example 'my-site/home'.");
        }
        var pagePath = path.Trim();

        if (confirmWithUser)
        {
            var confirmed = await ConfirmUnpublishPageAsync(pagePath);
            if (!confirmed)
            {
                return ToolResponse.Error("Unpublish cancelled by user.");
            }
        }

        var pageApiClient = GetRequiredService<IPageApiClient>();
        var unpublishResult = await pageApiClient.UnpublishPageAsync(pagePath);
        if (unpublishResult.IsFailure)
        {
            return ToolResponse.Error(unpublishResult);
        }

        var result = new WorkshopUnpublishPageResult(pagePath, true);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private async Task<bool> ConfirmUnpublishPageAsync(string path)
    {
        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
        var title = localizerService.GetString("Workshop_UnpublishPageConfirm_Title");
        var message = localizerService.GetString("Workshop_UnpublishPageConfirm_Message", path);

        return await ConfirmActionAsync(title, message);
    }
}
