using System.Text.Json;
using Celbridge.Workshop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A page entry in the workshop_list_pages result.
/// </summary>
public record class WorkshopListPagesEntry(
    string Path,
    string Url,
    DateTime PublishedAt,
    string PublishedBy,
    string ContentHash);

public partial class WorkshopTools
{
    /// <summary>List all pages published to the connected workshop.</summary>
    [McpServerTool(Name = "workshop_list_pages", ReadOnly = true)]
    [ToolAlias("workshop.list_pages")]
    [RelatedGuides("pages_overview")]
    public async partial Task<CallToolResult> ListPages()
    {
        var pageApiClient = GetRequiredService<IPageApiClient>();
        var listResult = await pageApiClient.ListPagesAsync();

        if (listResult.IsFailure)
        {
            return ToolResponse.Error(listResult);
        }

        var pages = new List<WorkshopListPagesEntry>();
        foreach (var page in listResult.Value)
        {
            var entry = new WorkshopListPagesEntry(
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
