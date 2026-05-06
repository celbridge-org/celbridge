using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Reads cell values from a sheet in an .xlsx workbook. By default returns row arrays from the
    /// sheet's used range, with the default page size of 1000 rows. Cells round-trip with their Excel
    /// type preserved. See spreadsheet_get_context for the JSON typing rules.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook to read.</param>
    /// <param name="sheet">Name of the worksheet to read.</param>
    /// <param name="range">A1-notation range to read (e.g. "B2:D10"). Empty string reads the sheet's used range. Do not include a sheet qualifier ("Sheet1!A1" is rejected).</param>
    /// <param name="mode">"values" (default) returns computed cell values. "formulas" returns the formula text (with leading '=') for cells that contain a formula.</param>
    /// <param name="headers">When true, the first row in the requested range becomes column names and each subsequent row is returned as an object keyed by header. Duplicate names get a numeric suffix. Empty headers become "column_&lt;letter&gt;".</param>
    /// <param name="offset">Number of data rows to skip before returning rows. Use 0 to start at the first data row.</param>
    /// <param name="limit">Maximum number of data rows to return. Use 0 to apply the default page size of 1000 rows.</param>
    /// <returns>JSON object with: rows (array of row arrays, or row objects when headers is true), totalRowCount (int), headers (array of resolved header names, empty when headers is false).</returns>
    [McpServerTool(Name = "spreadsheet_read_sheet", ReadOnly = true)]
    [ToolAlias("spreadsheet.read_sheet")]
    public partial CallToolResult ReadSheet(
        string resource,
        string sheet,
        string range = "",
        string mode = "values",
        bool headers = false,
        int offset = 0,
        int limit = 0)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }
        var workbookPath = resolveResult.Value;

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolError("Sheet name is required.");
        }

        if (!Enum.TryParse<SpreadsheetReadMode>(mode, ignoreCase: true, out var readMode))
        {
            return ToolError($"Invalid mode '{mode}'. Expected \"values\" or \"formulas\".");
        }

        var rangeArgument = string.IsNullOrEmpty(range) ? null : range;

        var options = new ReadOptions(
            Range: rangeArgument,
            Mode: readMode,
            Headers: headers,
            Offset: offset,
            Limit: limit);

        var reader = GetRequiredService<ISpreadsheetReader>();
        var readResult = reader.ReadSheet(workbookPath, sheet, options);
        if (readResult.IsFailure)
        {
            return ToolError(readResult);
        }

        var readValue = readResult.Value;
        var json = SerializeJson(readValue);
        return ToolSuccess(json);
    }
}
