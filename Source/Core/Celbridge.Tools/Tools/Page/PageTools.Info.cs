using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by page_info: a published page's served URL, publisher, and content hash.
/// </summary>
public record class PageInfoResult(
    string Path,
    string Url,
    DateTime PublishedAt,
    string PublishedBy,
    string ContentHash);

public partial class PageTools
{
    /// <summary>Inspect a published workshop page by its served path: its URL, publisher, and content hash.</summary>
    [McpServerTool(Name = "page_info", ReadOnly = true)]
    [ToolAlias("page.info")]
    [RelatedGuides("pages_overview")]
    public async partial Task<CallToolResult> Info(string path)
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

        var result = new PageInfoResult(
            page.Path,
            page.Url,
            page.PublishedAt,
            page.PublishedBy,
            page.ContentHash);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
