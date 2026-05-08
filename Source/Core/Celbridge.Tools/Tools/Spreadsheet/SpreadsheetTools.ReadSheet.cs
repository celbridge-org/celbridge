using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Read cell values or formulas from a sheet range, with paging and optional headers.</summary>
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
