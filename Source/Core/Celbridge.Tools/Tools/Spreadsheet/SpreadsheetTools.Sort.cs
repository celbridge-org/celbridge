using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Sorts the rows of a range by one or more columns, applied in order (earlier keys primary, later keys tiebreak).
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Worksheet whose range should be sorted. Required.</param>
    /// <param name="range">A1 cell range. Empty sorts the used range.</param>
    /// <param name="sortByJson">JSON array of sort keys with column (letter or 1-based number) and ascending (bool). At least one key required. Columns must lie inside the sort range.</param>
    /// <param name="hasHeaderRow">If true, the first row stays in place and is excluded from the sort.</param>
    /// <param name="matchCase">If true, text comparisons are case-sensitive.</param>
    /// <returns>JSON object with rowCount (excluding the header when hasHeaderRow is true).</returns>
    [McpServerTool(Name = "spreadsheet_sort")]
    [ToolAlias("spreadsheet.sort")]
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
            return ToolError(resolveResult);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolError("Sheet name is required.");
        }

        var parseResult = ParseSortKeys(sortByJson);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
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
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
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
