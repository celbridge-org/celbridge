namespace Celbridge.Spreadsheet;

/// <summary>
/// Selects whether ISpreadsheetReader.ReadSheet returns computed cell values or
/// the underlying formula text for cells that contain a formula.
/// </summary>
public enum SpreadsheetReadMode
{
    /// <summary>
    /// Return the cached computed value for each cell. Cells without a formula
    /// return their literal value.
    /// </summary>
    Values,

    /// <summary>
    /// Return the formula text (with the leading '=') for cells that contain a
    /// formula. Cells without a formula return their literal value.
    /// </summary>
    Formulas
}

/// <summary>
/// Optional parameters for ISpreadsheetReader.ReadSheet. Range is A1 notation
/// without a sheet qualifier (e.g. "B2:D10"). Null reads the sheet's used range.
/// Headers true treats the first row in the range as column names and emits
/// row objects keyed by header. Offset and Limit page large sheets. Limit zero
/// applies the reader's default page size.
/// </summary>
public record ReadOptions(
    string? Range = null,
    SpreadsheetReadMode Mode = SpreadsheetReadMode.Values,
    bool Headers = false,
    int Offset = 0,
    int Limit = 0);

/// <summary>
/// Result of ISpreadsheetReader.ReadSheet. Rows is either a list of row arrays
/// (when Headers was false) or a list of row dictionaries keyed by header
/// (when Headers was true). Both shapes are JSON-serialised via object?.
/// TotalRowCount is the total number of data rows the requested range would
/// produce ignoring offset and limit, so the caller can decide whether to page.
/// Headers carries the resolved header names when Headers was requested,
/// otherwise it is empty.
/// </summary>
public record ReadResult(
    IReadOnlyList<object?> Rows,
    int TotalRowCount,
    IReadOnlyList<string> Headers);

/// <summary>
/// Summary metadata for a single worksheet inside an .xlsx workbook.
/// Position is the 1-based tab position. UsedRange is the A1-notation range
/// that bounds all non-empty cells, or null if the sheet has no used range.
/// FrozenRows and FrozenColumns are the frozen-pane counts (zero when the
/// sheet has no frozen rows or columns on that axis).
/// </summary>
public record SheetInfo(
    string Name,
    int Position,
    string? UsedRange,
    int RowCount,
    int ColumnCount,
    int FrozenRows,
    int FrozenColumns);

/// <summary>
/// A defined name in the workbook. Scope is either "workbook" or the name of
/// the worksheet that owns the name.
/// </summary>
public record NamedRange(
    string Name,
    string RefersTo,
    string Scope);

/// <summary>
/// Workbook overview returned by ISpreadsheetReader.GetInfo. Lists every sheet
/// with its used range and dimensions, plus any defined named ranges.
/// </summary>
public record WorkbookInfo(
    IReadOnlyList<SheetInfo> Sheets,
    IReadOnlyList<NamedRange> NamedRanges);

/// <summary>
/// Result of ISpreadsheetReader.ExportCsv. Csv is the RFC 4180 encoded text
/// and is empty when the requested range is empty. RowCount and ColumnCount
/// are the dimensions of the source range so callers can report a summary
/// without re-parsing the CSV (which would mis-count rows that contain
/// embedded line breaks).
/// </summary>
public record ExportCsvResult(string Csv, int RowCount, int ColumnCount);

/// <summary>
/// Holds the sheet-qualified range address and the per-cell format specs
/// returned by ISpreadsheetReader.ReadFormat.
/// </summary>
public record ReadFormatResult(
    string Range,
    List<List<FormatSpec>> Rows);

/// <summary>
/// Search options for ISpreadsheetReader.Find. Sheet may be empty to search
/// every worksheet; Range may be empty to search the chosen sheet's entire
/// used range. Find must be non-empty.
/// </summary>
public record FindOptions(
    string Find,
    string Sheet,
    string Range,
    bool MatchCase,
    bool MatchEntireCellContents);

/// <summary>
/// One match from ISpreadsheetReader.Find. Sheet is the worksheet name. Cell
/// is the A1 cell address. Text is the cell's literal text or formula
/// expression that contained the match. IsFormula is true when the match
/// was found inside a formula cell's expression text.
/// </summary>
public record FindMatch(
    string Sheet,
    string Cell,
    string Text,
    bool IsFormula);

/// <summary>
/// Result of ISpreadsheetReader.Find. Matches lists every cell whose text or
/// formula expression contained the search string. MatchCount is the size of
/// Matches (provided so a JSON consumer does not have to count the array).
/// </summary>
public record FindResult(
    IReadOnlyList<FindMatch> Matches,
    int MatchCount);

/// <summary>
/// Workbook view-state snapshot returned by ISpreadsheetReader.GetActiveView.
/// Sheet is the active worksheet name. Ranges is the full selection in A1
/// notation, with at least one entry; multiple entries represent the
/// non-contiguous selection a user can make with Ctrl+click in the editor.
/// Range is a convenience equal to Ranges[0]. ActiveCell is the anchor cell
/// within the selection (Excel's white cell), present when there is a
/// selection and equal to Range when Range is a single cell. TopLeftCell is
/// the scroll anchor, the cell pinned at the upper-left of the visible
/// viewport.
/// </summary>
public record ActiveView(
    string Sheet,
    string Range,
    IReadOnlyList<string> Ranges,
    string ActiveCell,
    string TopLeftCell);

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
    Result<WorkbookInfo> GetInfo(string workbookPath);

    /// <summary>
    /// Reads cell values from a sheet. When options.Range is null the sheet's
    /// used range is read. An empty sheet returns Rows = [] and TotalRowCount = 0.
    /// </summary>
    Result<ReadResult> ReadSheet(string workbookPath, string sheetName, ReadOptions options);

    /// <summary>
    /// Returns the contents of a sheet as RFC 4180 CSV text along with the row
    /// and column dimensions of the source range. Range is optional A1 notation.
    /// Null exports the sheet's used range. An empty range returns an empty Csv
    /// string and zero dimensions.
    /// </summary>
    Result<ExportCsvResult> ExportCsv(string workbookPath, string sheetName, string? range);

    /// <summary>
    /// Reads cell formatting from a sheet as FormatSpec objects in
    /// the same shape accepted by spreadsheet_format_ranges. When range is null
    /// the sheet's used range is read. An empty sheet returns Rows = [].
    /// </summary>
    Result<ReadFormatResult> ReadFormat(string workbookPath, string sheetName, string? range);

    /// <summary>
    /// Returns the workbook's persisted view state: active sheet, selection on
    /// that sheet, active cell within the selection, and scroll anchor. Mirrors
    /// the parameters accepted by ISetActiveViewCommand so callers
    /// can round-trip the view state.
    /// </summary>
    Result<ActiveView> GetActiveView(string workbookPath);

    /// <summary>
    /// Searches the workbook for cells whose literal text or formula expression
    /// contains the search string. Numeric, boolean and date cells are skipped.
    /// Empty Sheet searches every worksheet; empty Range searches each chosen
    /// sheet's entire used range.
    /// </summary>
    Result<FindResult> Find(string workbookPath, FindOptions options);
}
