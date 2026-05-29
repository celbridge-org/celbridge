using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Find cells whose text or formula contains a substring across one or all sheets.</summary>
    [McpServerTool(Name = "spreadsheet_find", ReadOnly = true)]
    [ToolAlias("spreadsheet.find")]
    [RelatedGuides("resource_keys", "spreadsheet_a1_notation", "spreadsheet_cell_typing")]
    public async partial Task<CallToolResult> Find(
        string resource,
        string find,
        string sheet = "",
        string range = "",
        bool matchCase = false,
        bool matchEntireCellContents = false)
    {
        var resolveResult = await ResolveWorkbookResourceAsync(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        if (string.IsNullOrEmpty(find))
        {
            return ToolResponse.Error("Find text is required and must be non-empty.");
        }

        var openResult = await OpenWorkbookStreamAsync(workbookResource);
        if (openResult.IsFailure)
        {
            return ToolResponse.Error(openResult);
        }

        using var stream = openResult.Value;
        var reader = GetRequiredService<ISpreadsheetReader>();
        var options = new FindOptions(find, sheet, range, matchCase, matchEntireCellContents);
        var findResult = reader.Find(stream, options);
        if (findResult.IsFailure)
        {
            return ToolResponse.Error(findResult);
        }

        var findValue = findResult.Value;
        var json = SerializeJson(findValue);
        return ToolResponse.Success(json);
    }
}
