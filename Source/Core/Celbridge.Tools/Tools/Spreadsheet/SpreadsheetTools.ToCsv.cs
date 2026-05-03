using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Exports a sheet (or a sub-range of one) as RFC 4180 CSV text. Comma delimiter, double-quote
    /// quoting, embedded quotes doubled, CRLF line endings between rows. With no destination the
    /// CSV is returned inline. With a destination resource key the CSV is written to that file via
    /// the audited file-write command and a one-line summary is returned instead of the body, so a
    /// large export does not have to round-trip through the agent's context.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to export.</param>
    /// <param name="range">A1-notation range to export (e.g. "B2:D10"). Empty string exports the sheet's used range.</param>
    /// <param name="destination">Optional resource key of a file to write the CSV to. Empty string returns the CSV inline. When set, the file is created or overwritten and the response is a one-line summary (rows, bytes, destination).</param>
    /// <returns>The CSV text when destination is empty, otherwise a one-line summary like "Wrote 1234 rows (45 KB) to data/export.csv". Empty CSV when the sheet (or requested range) is empty.</returns>
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

        var byteCount = System.Text.Encoding.UTF8.GetByteCount(csv.Csv);
        var summary = $"Wrote {csv.RowCount} rows ({FormatByteCount(byteCount)}) to {destination}";
        return SuccessResult(summary);
    }

    private static string FormatByteCount(int byteCount)
    {
        const int KiB = 1024;
        const int MiB = 1024 * 1024;

        if (byteCount < KiB)
        {
            return $"{byteCount} B";
        }
        if (byteCount < MiB)
        {
            var kibibytes = byteCount / (double)KiB;
            return $"{kibibytes:F1} KB";
        }

        var mebibytes = byteCount / (double)MiB;
        return $"{mebibytes:F1} MB";
    }
}
