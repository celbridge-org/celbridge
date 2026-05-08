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
    /// <summary>
    /// Exports a sheet (or a sub-range) as RFC 4180 CSV text.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Worksheet to export.</param>
    /// <param name="range">A1-notation range. Empty string exports the sheet's used range.</param>
    /// <param name="destination">Optional resource key to write the CSV to. Empty returns CSV inline; non-empty returns metadata. See guides_read(['spreadsheet_export_csv']).</param>
    /// <returns>CSV text when destination is empty, otherwise a JSON object with rowCount, columnCount, byteCount, and destination.</returns>
    [McpServerTool(Name = "spreadsheet_export_csv")]
    [ToolAlias("spreadsheet.export_csv")]
    public async partial Task<CallToolResult> ExportCsv(string resource, string sheet, string range = "", string destination = "")
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

        var rangeArgument = string.IsNullOrEmpty(range) ? null : range;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var csvResult = reader.ExportCsv(workbookPath, sheet, rangeArgument);
        if (csvResult.IsFailure)
        {
            return ToolError(csvResult);
        }
        var csv = csvResult.Value;

        if (string.IsNullOrEmpty(destination))
        {
            return ToolSuccess(csv.Csv);
        }

        if (!ResourceKey.TryCreate(destination, out var destinationResourceKey))
        {
            return ToolError($"Invalid destination resource key: '{destination}'");
        }

        var writeResult = await ExecuteCommandAsync<IWriteFileCommand>(command =>
        {
            command.FileResource = destinationResourceKey;
            command.Content = csv.Csv;
        });
        if (writeResult.IsFailure)
        {
            return ToolError(writeResult);
        }

        var byteCount = Encoding.UTF8.GetByteCount(csv.Csv);
        var metadata = new ExportCsvFileResult(csv.RowCount, csv.ColumnCount, byteCount, destination);
        var json = SerializeJson(metadata);
        return ToolSuccess(json);
    }
}
