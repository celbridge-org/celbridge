using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_sort: the number of rows that were
/// re-ordered.
/// </summary>
public record class SortResult(int RowCount);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Sorts the rows of a cell range by one or more columns, in order. Multiple sort keys are
    /// applied in the order they appear in sortByJson — earlier keys are primary, later keys
    /// break ties. When hasHeaderRow is true, the first row of the range stays in place and
    /// only the rows below it are sorted. Columns in sortByJson are absolute (sheet column
    /// letters or 1-based numbers); they must lie inside the sort range. Formulas inside the
    /// sorted rows have their cell references shifted by Excel's own sort logic.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet whose range should be sorted. Required.</param>
    /// <param name="range">A1 cell range to sort (e.g. "A2:F100"). Empty string sorts the worksheet's entire used range. Do not include a sheet qualifier.</param>
    /// <param name="sortByJson">JSON array of sort keys, applied in order. Each key is an object with column (string, A1 column letter "B" or 1-based number "2") and ascending (bool) fields. Must contain at least one key.</param>
    /// <param name="hasHeaderRow">If true, the first row of the range is treated as a header and is excluded from the sort. Default false.</param>
    /// <param name="matchCase">If true, text comparisons are case-sensitive. Default false (matches Excel).</param>
    /// <returns>JSON object with field: rowCount (int, the number of rows sorted, excluding the header row when hasHeaderRow is true).</returns>
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
        var commandResult = await ExecuteCommandAsync<ISpreadsheetSortCommand, SpreadsheetSortResult>(command =>
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
        var result = new SortResult(commandValue.RowCount);

        return ToolSuccess(SerializeJson(result));
    }

    private static Result<IReadOnlyList<SpreadsheetSortKey>> ParseSortKeys(string sortByJson)
    {
        if (string.IsNullOrEmpty(sortByJson))
        {
            return Result<IReadOnlyList<SpreadsheetSortKey>>.Fail("sortByJson is required.");
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var keys = JsonSerializer.Deserialize<List<SpreadsheetSortKey>>(sortByJson, options);
            if (keys is null)
            {
                return Result<IReadOnlyList<SpreadsheetSortKey>>.Fail("sortByJson must be a non-null array.");
            }
            if (keys.Count == 0)
            {
                return Result<IReadOnlyList<SpreadsheetSortKey>>.Fail("sortByJson must contain at least one sort key.");
            }
            return Result<IReadOnlyList<SpreadsheetSortKey>>.Ok(keys);
        }
        catch (JsonException ex)
        {
            return Result<IReadOnlyList<SpreadsheetSortKey>>.Fail($"Invalid sortByJson: {ex.Message}");
        }
    }
}
