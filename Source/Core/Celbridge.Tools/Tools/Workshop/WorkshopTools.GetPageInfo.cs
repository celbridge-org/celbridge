using System.Text.Json;
using Celbridge.Workshop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by workshop_get_page_info: a published page's served URL, publisher, and content hash.
/// </summary>
public record class WorkshopGetPageInfoResult(
    string Path,
    string Url,
    DateTime PublishedAt,
    string PublishedBy,
    string ContentHash);

public partial class WorkshopTools
{
    /// <summary>Inspect a published workshop page by its served path: its URL, publisher, and content hash.</summary>
    [McpServerTool(Name = "workshop_get_page_info", ReadOnly = true)]
    [ToolAlias("workshop.get_page_info")]
    [RelatedGuides("pages_overview")]
    public async partial Task<CallToolResult> GetPageInfo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResponse.Error("A page path is required, for example 'my-site/home'.");
        }

        var pageApiClient = GetRequiredService<IPageApiClient>();
        var pageResult = await pageApiClient.GetPageAsync(path.Trim());
        if (pageResult.IsFailure)
        {
            return ToolResponse.Error(pageResult);
        }
        var page = pageResult.Value;

        var result = new WorkshopGetPageInfoResult(
            page.Path,
            page.Url,
            page.PublishedAt,
            page.PublishedBy,
            page.ContentHash);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
