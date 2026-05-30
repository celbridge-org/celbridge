using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Sort the rows of a range by one or more column keys, with optional header row.</summary>
    [McpServerTool(Name = "spreadsheet_sort")]
    [ToolAlias("spreadsheet.sort")]
    [RelatedGuides("resource_keys", "spreadsheet_a1_notation", "spreadsheet_headers_mode", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> Sort(
        string resource,
        string sheet,
        string range,
        string sortByJson,
        bool hasHeaderRow = false,
        bool matchCase = false)
    {
        var resolveResult = await ResolveWorkbookResourceAsync(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.");
        }

        var parseResult = ParseJsonArgument<List<SortKey>>(sortByJson, "sortByJson");
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var sortKeys = parseResult.Value;
        if (sortKeys.Count == 0)
        {
            return ToolResponse.Error("sortByJson must contain at least one sort key.");
        }

        var commandResult = await ExecuteCommandAsync<ISortRangeCommand, SortRangeResult>(command =>
        {
            command.FileResource = workbookResource;
            command.Sheet = sheet;
            command.Range = range;
            command.SortKeys = sortKeys;
            command.HasHeaderRow = hasHeaderRow;
            command.MatchCase = matchCase;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolResponse.Success(json);
    }

}
