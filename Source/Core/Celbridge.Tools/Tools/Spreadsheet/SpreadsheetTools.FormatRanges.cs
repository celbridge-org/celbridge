using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_format_ranges: how many edits were applied,
/// how many top-level format properties were applied across them, and whether
/// any edit triggered AdjustToContents via autoFitColumns.
/// </summary>
public record class FormatRangesResult(int EditsApplied, int PropertiesApplied, bool AutoFitApplied);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Applies a batch of format edits to one workbook in a single open/save cycle. Each edit specifies
    /// a sheet, a range (A1 cell range, column letter or range, or row number or range), and a format
    /// spec. Edits may target different sheets. Only fields present in each edit's format are applied,
    /// preserving other formatting on cells the target does not cover. Edits run in order. If any edit
    /// fails, the whole batch fails and nothing is saved. Call spreadsheet_get_context for the format
    /// spec shape and supported values.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="editsJson">JSON array of edits. Each edit is an object with sheet (string), range (string), and format (object) fields. The format object accepts the same keys as spreadsheet_get_context describes for the format spec (textFormat, backgroundColor, borders, horizontalAlignment, verticalAlignment, wrapText, numberFormat, columnWidth, rowHeight, autoFitColumns, mergeRange). columnWidth is in Excel character units (NOT pixels): default is 8.43, typical column is 10 to 60, anything above 100 is almost certainly wrong. Use autoFitColumns: true to fit width to content automatically. rowHeight is in points: default is 15, typical row is 12 to 30. To clear a colour or reset a value back to the workbook default, pass the empty string for colour fields (backgroundColor, foregroundColor, border colour) or the empty string for fontFamily, a non-positive number for fontSize, a negative number for columnWidth or rowHeight, and false for mergeRange to unmerge an existing merge.</param>
    /// <returns>JSON object with fields: editsApplied (int), propertiesApplied (int, summed across edits), autoFitApplied (bool, true if any edit triggered AdjustToContents).</returns>
    [McpServerTool(Name = "spreadsheet_format_ranges")]
    [ToolAlias("spreadsheet.format_ranges")]
    public async partial Task<CallToolResult> FormatRanges(string resource, string editsJson)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ErrorResult(resolveResult.FirstErrorMessage);
        }

        var parseResult = ParseFormatEdits(editsJson);
        if (parseResult.IsFailure)
        {
            return ErrorResult(parseResult.FirstErrorMessage);
        }
        var edits = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var (callResult, commandResult) = await ExecuteCommandAsync<ISpreadsheetFormatRangesCommand, SpreadsheetFormatRangesResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Edits = edits;
        });
        if (callResult.IsError == true)
        {
            return callResult;
        }

        var commandValue = commandResult ?? new SpreadsheetFormatRangesResult(0, 0, false);
        var result = new FormatRangesResult(commandValue.EditsApplied, commandValue.PropertiesApplied, commandValue.AutoFitApplied);

        return SuccessResult(SerializeJson(result));
    }

    private static Result<IReadOnlyList<SpreadsheetFormatEdit>> ParseFormatEdits(string editsJson)
    {
        if (string.IsNullOrEmpty(editsJson))
        {
            return Result<IReadOnlyList<SpreadsheetFormatEdit>>.Fail("Edits JSON is required.");
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var edits = JsonSerializer.Deserialize<List<SpreadsheetFormatEdit>>(editsJson, options);
            if (edits is null)
            {
                return Result<IReadOnlyList<SpreadsheetFormatEdit>>.Fail("Edits JSON must be a non-null array.");
            }
            if (edits.Count == 0)
            {
                return Result<IReadOnlyList<SpreadsheetFormatEdit>>.Fail("Edits array must contain at least one edit.");
            }
            return Result<IReadOnlyList<SpreadsheetFormatEdit>>.Ok(edits);
        }
        catch (JsonException ex)
        {
            return Result<IReadOnlyList<SpreadsheetFormatEdit>>.Fail($"Invalid edits JSON: {ex.Message}");
        }
    }
}
