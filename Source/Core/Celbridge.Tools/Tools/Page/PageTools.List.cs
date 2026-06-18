using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A page entry in the page_list result.
/// </summary>
public record class PageListEntry(
    string Path,
    string Url,
    DateTime PublishedAt,
    string PublishedBy,
    string ContentHash);

public partial class PageTools
{
    /// <summary>List all pages published to the connected workshop.</summary>
    [McpServerTool(Name = "page_list", ReadOnly = true)]
    [ToolAlias("page.list")]
    [RelatedGuides("pages_overview")]
    public async partial Task<CallToolResult> List()
    {
        var pageApiClient = GetRequiredService<IPageApiClient>();
        var listResult = await pageApiClient.ListPagesAsync();

        if (listResult.IsFailure)
        {
            return ToolResponse.Error(listResult);
        }

        var pages = new List<PageListEntry>();
        foreach (var page in listResult.Value)
        {
            var entry = new PageListEntry(
                page.Path,
                page.Url,
                page.PublishedAt,
                page.PublishedBy,
                page.ContentHash);
            pages.Add(entry);
        }

        var json = JsonSerializer.Serialize(pages, JsonOptions);
        return ToolResponse.Success(json);
    }
}
