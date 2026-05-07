using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

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
    /// <returns>JSON object with fields: matches (array of {sheet, cell, text, isFormula}), matchCount (int). For formula cells, text is the formula expression without the leading '=' (e.g. "SUM(C2:F2)" for the cell =SUM(C2:F2)).</returns>
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
            return ToolError(resolveResult);
        }
        var workbookPath = resolveResult.Value;

        if (string.IsNullOrEmpty(find))
        {
            return ToolError("Find text is required and must be non-empty.");
        }

        var reader = GetRequiredService<ISpreadsheetReader>();
        var options = new FindOptions(find, sheet, range, matchCase, matchEntireCellContents);
        var findResult = reader.Find(workbookPath, options);
        if (findResult.IsFailure)
        {
            return ToolError(findResult);
        }

        var findValue = findResult.Value;
        var json = SerializeJson(findValue);
        return ToolSuccess(json);
    }
}
