using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// One match in spreadsheet_find's response. cell is the A1 address on the
/// named sheet. text is the literal cell text or the formula expression that
/// contained the match. isFormula is true when the match was inside a
/// formula's expression text rather than a value cell.
/// </summary>
public record class SpreadsheetFindMatchDto(string Sheet, string Cell, string Text, bool IsFormula);

/// <summary>
/// Result returned by spreadsheet_find: every cell whose text or formula
/// expression contained the search string, plus the count.
/// </summary>
public record class SpreadsheetFindToolResult(IReadOnlyList<SpreadsheetFindMatchDto> Matches, int MatchCount);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Searches a workbook for cells whose text or formula expression contains a search string.
    /// Returns the list of matches without modifying the workbook. Use this to identify cells
    /// to act on (e.g. before a targeted spreadsheet_write_cells), without slurping the whole
    /// sheet via spreadsheet_read_sheet. Numeric, boolean and date cells are skipped — only
    /// text-bearing cells and formula cells are searched. For formula cells, the search is
    /// performed against the formula expression text (so SUM(A1:A10) would match "A1:A10" or
    /// "SUM"). Empty sheet searches every worksheet; empty range searches the chosen sheet's
    /// entire used range. range can only be used together with a specific sheet.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="find">Substring to search for. Required and non-empty.</param>
    /// <param name="sheet">Name of the worksheet to search. Empty string searches every worksheet in the workbook.</param>
    /// <param name="range">Optional A1 range to limit the search ("A1:C100", "B", "B:D", "3", "3:10"). Empty string searches the entire used range of the chosen sheet. Only valid when sheet is also specified. Do not include a sheet qualifier.</param>
    /// <param name="matchCase">If true, the search is case-sensitive. Default false.</param>
    /// <param name="matchEntireCellContents">If true, find must equal the cell's full text. If false, find matches as a substring. Default false.</param>
    /// <returns>JSON object with fields: matches (array of {sheet, cell, text, isFormula}), matchCount (int).</returns>
    [McpServerTool(Name = "spreadsheet_find", ReadOnly = true)]
    [ToolAlias("spreadsheet.find")]
    public partial CallToolResult Find(
        string resource,
        string find,
        string sheet = "",
        string range = "",
        bool matchCase = false,
        bool matchEntireCellContents = false)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ErrorResult(resolveResult);
        }
        var workbookPath = resolveResult.Value;

        if (string.IsNullOrEmpty(find))
        {
            return ErrorResult("Find text is required and must be non-empty.");
        }

        var reader = GetRequiredService<ISpreadsheetReader>();
        var options = new SpreadsheetFindOptions(find, sheet, range, matchCase, matchEntireCellContents);
        var findResult = reader.Find(workbookPath, options);
        if (findResult.IsFailure)
        {
            return ErrorResult(findResult);
        }

        var commandValue = findResult.Value;
        var matches = commandValue.Matches
            .Select(match => new SpreadsheetFindMatchDto(match.Sheet, match.Cell, match.Text, match.IsFormula))
            .ToList();
        var result = new SpreadsheetFindToolResult(matches, commandValue.MatchCount);

        return SuccessResult(SerializeJson(result));
    }
}
