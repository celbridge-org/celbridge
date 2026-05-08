using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Reads cell values from a sheet. Cells round-trip with their Excel type preserved.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Worksheet to read.</param>
    /// <param name="range">A1 range. Empty reads the sheet's used range. Sheet qualifiers are rejected.</param>
    /// <param name="mode">"values" returns computed values; "formulas" returns formula text with leading '='.</param>
    /// <param name="headers">When true, the first row becomes column names and each row is returned as an object.</param>
    /// <param name="offset">Data rows to skip before returning rows.</param>
    /// <param name="limit">Maximum data rows to return. 0 applies the default page size of 1000.</param>
    /// <param name="columnLimit">Maximum columns per row. 0 applies the default cap of 256.</param>
    /// <returns>JSON object with rows, totalRowCount, totalColumnCount, and resolved headers. See guides_read(['spreadsheet_read_sheet']).</returns>
    [McpServerTool(Name = "spreadsheet_read_sheet", ReadOnly = true)]
    [ToolAlias("spreadsheet.read_sheet")]
    public partial CallToolResult ReadSheet(
        string resource,
        string sheet,
        string range = "",
        string mode = "values",
        bool headers = false,
        int offset = 0,
        int limit = 0,
        int columnLimit = 0)
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
            Limit: limit,
            ColumnLimit: columnLimit);

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
