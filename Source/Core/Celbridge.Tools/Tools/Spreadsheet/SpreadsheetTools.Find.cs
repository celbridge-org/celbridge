using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Searches a workbook for cells whose text or formula expression contains a substring.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="find">Substring to search for. Required and non-empty.</param>
    /// <param name="sheet">Worksheet to search. Empty searches every worksheet.</param>
    /// <param name="range">Optional A1 range to limit the search. Only valid when sheet is also specified.</param>
    /// <param name="matchCase">If true, the search is case-sensitive.</param>
    /// <param name="matchEntireCellContents">If true, find must equal the cell's full text rather than matching as a substring.</param>
    /// <returns>JSON object with matches (array of {sheet, cell, text, isFormula}) and matchCount. See guides_read(['spreadsheet_find']).</returns>
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
