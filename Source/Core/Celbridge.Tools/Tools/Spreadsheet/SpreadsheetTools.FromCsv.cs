using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_from_csv: the dimensions of the imported
/// block and whether a new sheet was created by this call.
/// </summary>
public record class FromCsvResult(int RowCount, int ColumnCount, bool SheetCreated);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Replaces the contents of a worksheet with parsed CSV data starting at A1. The CSV is parsed
    /// per RFC 4180 (comma delimiter, double-quote quoting, embedded quotes doubled, CRLF or LF line
    /// endings). Existing cells in the sheet are cleared before the CSV block is written. Other sheets
    /// in the workbook are untouched. CSV imports do not produce formula cells, but formulas elsewhere
    /// in the workbook are recalculated as part of the save.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to populate.</param>
    /// <param name="csvText">RFC 4180 CSV text to import.</param>
    /// <param name="createIfMissing">When true, the sheet is created if it does not exist. When false, a missing sheet returns an error.</param>
    /// <returns>JSON object with fields: rowCount (int), columnCount (int), sheetCreated (bool).</returns>
    [McpServerTool(Name = "spreadsheet_from_csv")]
    [ToolAlias("spreadsheet.from_csv")]
    public async partial Task<CallToolResult> FromCsv(string resource, string sheet, string csvText, bool createIfMissing = false)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ErrorResult(resolveResult.FirstErrorMessage);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ErrorResult("Sheet name is required.");
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var (callResult, commandResult) = await ExecuteCommandAsync<ISpreadsheetImportCsvCommand, SpreadsheetImportCsvResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.CsvText = csvText ?? string.Empty;
            command.CreateIfMissing = createIfMissing;
        });
        if (callResult.IsError == true)
        {
            return callResult;
        }

        var commandValue = commandResult ?? new SpreadsheetImportCsvResult(0, 0, false);
        var result = new FromCsvResult(commandValue.RowCount, commandValue.ColumnCount, commandValue.SheetCreated);
        return SuccessResult(SerializeJson(result));
    }
}
