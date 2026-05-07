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
    /// Exports a sheet (or a sub-range of one) as RFC 4180 CSV text. Comma delimiter, double-quote
    /// quoting, embedded quotes doubled, CRLF line endings between rows. With no destination the
    /// CSV body is returned inline. With a destination resource key the CSV is written to that file
    /// via the audited file-write command and a small JSON metadata object is returned instead of
    /// the body, so a large export does not have to round-trip through the agent or script context.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to export.</param>
    /// <param name="range">A1-notation range to export (e.g. "B2:D10"). Empty string exports the sheet's used range.</param>
    /// <param name="destination">Optional resource key of a file to write the CSV to. Empty string returns the CSV inline. When set, the file is created or overwritten and the response is a JSON object with rowCount, columnCount, byteCount, and destination fields.</param>
    /// <returns>The CSV text when destination is empty, otherwise a JSON object with fields: rowCount (int), columnCount (int), byteCount (int), destination (string). When the sheet or requested range is empty, the inline response is an empty body and the file destination is a zero-byte file; the metadata in the file case reports rowCount and columnCount of zero.</returns>
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
