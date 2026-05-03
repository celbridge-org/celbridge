using System.Text;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_to_csv when a destination is supplied. Reports
/// the dimensions of the source range and the byte size of the CSV that was
/// written so callers (including cel proxy clients) can act on the metadata
/// without parsing a free-form summary.
/// </summary>
public record class WriteCsvResult(int RowCount, int ColumnCount, int ByteCount, string Destination);

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
    /// <returns>The CSV text when destination is empty, otherwise a JSON object with fields: rowCount (int), columnCount (int), byteCount (int), destination (string). Empty CSV when the sheet (or requested range) is empty.</returns>
    [McpServerTool(Name = "spreadsheet_to_csv")]
    [ToolAlias("spreadsheet.to_csv")]
    public async partial Task<CallToolResult> ToCsv(string resource, string sheet, string range = "", string destination = "")
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ErrorResult(resolveResult.FirstErrorMessage);
        }
        var workbookPath = resolveResult.Value;

        if (string.IsNullOrEmpty(sheet))
        {
            return ErrorResult("Sheet name is required.");
        }

        var rangeArgument = string.IsNullOrEmpty(range) ? null : range;

        var reader = GetRequiredService<ISpreadsheetReader>();
        var csvResult = reader.ToCsv(workbookPath, sheet, rangeArgument);
        if (csvResult.IsFailure)
        {
            return ErrorResult(csvResult.FirstErrorMessage);
        }
        var csv = csvResult.Value;

        if (string.IsNullOrEmpty(destination))
        {
            return SuccessResult(csv.Csv);
        }

        if (!ResourceKey.TryCreate(destination, out var destinationResourceKey))
        {
            return ErrorResult($"Invalid destination resource key: '{destination}'");
        }

        var writeResult = await ExecuteCommandAsync<IWriteFileCommand>(command =>
        {
            command.FileResource = destinationResourceKey;
            command.Content = csv.Csv;
        });
        if (writeResult.IsError == true)
        {
            return writeResult;
        }

        var byteCount = Encoding.UTF8.GetByteCount(csv.Csv);
        var metadata = new WriteCsvResult(csv.RowCount, csv.ColumnCount, byteCount, destination);
        return SuccessResult(SerializeJson(metadata));
    }
}
