using System.Text.Json;
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
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.");
        }

        var parseResult = ParseSortKeys(sortByJson);
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var sortKeys = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISortRangeCommand, SortRangeResult>(command =>
        {
            command.FileResource = fileResourceKey;
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

    private static Result<IReadOnlyList<SortKey>> ParseSortKeys(string sortByJson)
    {
        if (string.IsNullOrEmpty(sortByJson))
        {
            return Result.Fail("sortByJson is required.");
        }

        try
        {
            var keys = JsonSerializer.Deserialize<List<SortKey>>(sortByJson, JsonOptions);
            if (keys is null)
            {
                return Result.Fail("sortByJson must be a non-null array.");
            }
            if (keys.Count == 0)
            {
                return Result.Fail("sortByJson must contain at least one sort key.");
            }
            return keys;
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Invalid sortByJson: {ex.Message}");
        }
    }
}
