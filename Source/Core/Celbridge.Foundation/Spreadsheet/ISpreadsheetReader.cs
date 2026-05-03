namespace Celbridge.Spreadsheet;

/// <summary>
/// Reads .xlsx workbooks from disk for the spreadsheet_* MCP tools. The reader
/// is stateless and opens the workbook fresh on every call. Callers pass the
/// absolute filesystem path resolved from a resource key. All methods report
/// expected failures (missing sheet, malformed range) as Result.Fail rather
/// than throwing.
/// </summary>
public interface ISpreadsheetReader
{
    /// <summary>
    /// Returns a workbook overview: every sheet with its used range and
    /// dimensions, plus any workbook-scoped or sheet-scoped named ranges.
    /// </summary>
    Result<SpreadsheetWorkbookInfo> GetInfo(string workbookPath);

    /// <summary>
    /// Reads cell values from a sheet. When options.Range is null the sheet's
    /// used range is read. An empty sheet returns Rows = [] and TotalRowCount = 0.
    /// </summary>
    Result<SpreadsheetReadResult> ReadSheet(string workbookPath, string sheetName, SpreadsheetReadOptions options);

    /// <summary>
    /// Returns the contents of a sheet as RFC 4180 CSV text along with the row
    /// and column dimensions of the source range. Range is optional A1 notation.
    /// Null exports the sheet's used range. An empty range returns an empty Csv
    /// string and zero dimensions.
    /// </summary>
    Result<SpreadsheetCsvResult> ToCsv(string workbookPath, string sheetName, string? range);
}
