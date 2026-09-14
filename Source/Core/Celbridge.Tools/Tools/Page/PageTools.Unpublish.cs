using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by page_unpublish confirming the page's served content was removed.
/// </summary>
public record class PageUnpublishResult(string Path, bool Unpublished);

public partial class PageTools
{
    /// <summary>Unpublish a page from the workshop, removing its served content.</summary>
    [McpServerTool(Name = "page_unpublish", Destructive = true)]
    [ToolAlias("page.unpublish")]
    [RelatedGuides("pages_overview", "silent_vs_interactive")]
    public async partial Task<CallToolResult> Unpublish(string path, bool confirmWithUser = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResponse.Error("A page path is required, for example 'my-site/home'.");
        }
        var pagePath = path.Trim();

        if (confirmWithUser)
        {
            var confirmed = await ConfirmUnpublishAsync(pagePath);
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

        var result = new PageUnpublishResult(pagePath, true);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private async Task<bool> ConfirmUnpublishAsync(string path)
    {
        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
        var title = localizerService.GetString("Page_UnpublishConfirm_Title");
        var message = localizerService.GetString("Page_UnpublishConfirm_Message", path);

        return await ConfirmActionAsync(title, message);
    }
}
