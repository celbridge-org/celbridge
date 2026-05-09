using System.Text;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Metadata returned by spreadsheet_export_csv when a destination is
/// supplied: source-range dimensions plus the byte size and resource key of
/// the CSV file the tool wrote. Distinct from the reader's inline-CSV
/// result — this describes the file, not the body.
/// </summary>
public record class ExportCsvFileResult(int RowCount, int ColumnCount, int ByteCount, string Destination);

public partial class SpreadsheetTools
{
    /// <summary>Export a sheet or sub-range as RFC 4180 CSV, returned inline or written to a file.</summary>
    [McpServerTool(Name = "spreadsheet_export_csv")]
    [ToolAlias("spreadsheet.export_csv")]
    public async partial Task<CallToolResult> ExportCsv(string resource, string sheet, string range = "", string destination = "")
    {
        const string ToolGuide = "spreadsheet_export_csv";

        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult, ToolGuide);
        }
        var workbookPath = resolveResult.Value;

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.", ToolGuide);
        }

        var rangeArgument = string.IsNullOrEmpty(range) ? null : range;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var csvResult = reader.ExportCsv(workbookPath, sheet, rangeArgument);
        if (csvResult.IsFailure)
        {
            return ToolResponse.Error(csvResult, ToolGuide);
        }
        var csv = csvResult.Value;

        if (string.IsNullOrEmpty(destination))
        {
            return ToolResponse.Success(csv.Csv);
        }

        if (!ResourceKey.TryCreate(destination, out var destinationResourceKey))
        {
            return ToolResponse.InvalidResourceKey(destination);
        }

        var writeResult = await ExecuteCommandAsync<IWriteFileCommand>(command =>
        {
            command.FileResource = destinationResourceKey;
            command.Content = csv.Csv;
        });
        if (writeResult.IsFailure)
        {
            return ToolResponse.Error(writeResult, ToolGuide);
        }

        var byteCount = Encoding.UTF8.GetByteCount(csv.Csv);
        var metadata = new ExportCsvFileResult(csv.RowCount, csv.ColumnCount, byteCount, destination);
        var json = SerializeJson(metadata);
        return ToolResponse.Success(json);
    }
}
