using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Read cell values or formulas from a sheet range, with paging and optional headers.</summary>
    [McpServerTool(Name = "spreadsheet_read_sheet", ReadOnly = true)]
    [ToolAlias("spreadsheet.read_sheet")]
    [RelatedGuides("resource_keys", "spreadsheet_a1_notation", "spreadsheet_cell_typing", "spreadsheet_headers_mode", "spreadsheet_paging")]
    public async partial Task<CallToolResult> ReadSheet(
        string resource,
        string sheet,
        string range = "",
        string mode = "values",
        bool headers = false,
        int offset = 0,
        int limit = 0,
        int columnLimit = 0)
    {
        var resolveResult = await ResolveWorkbookResourceAsync(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.");
        }

        if (!Enum.TryParse<SpreadsheetReadMode>(mode, ignoreCase: true, out var readMode))
        {
            return ToolResponse.Error($"Invalid mode '{mode}'. Expected \"values\" or \"formulas\".");
        }

        var rangeArgument = string.IsNullOrEmpty(range) ? null : range;

        var options = new ReadOptions(
            Range: rangeArgument,
            Mode: readMode,
            Headers: headers,
            Offset: offset,
            Limit: limit,
            ColumnLimit: columnLimit);

        var openResult = await OpenWorkbookStreamAsync(workbookResource);
        if (openResult.IsFailure)
        {
            return ToolResponse.Error(openResult);
        }

        using var stream = openResult.Value;
        var reader = GetRequiredService<ISpreadsheetReader>();
        var readResult = reader.ReadSheet(stream, sheet, options);
        if (readResult.IsFailure)
        {
            return ToolResponse.Error(readResult);
        }

        var readValue = readResult.Value;
        var json = SerializeJson(readValue);
        return ToolResponse.Success(json);
    }
}
