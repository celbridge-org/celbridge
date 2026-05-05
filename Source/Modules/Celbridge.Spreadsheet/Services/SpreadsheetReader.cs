using System.Text;
using Celbridge.Spreadsheet.Commands;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Services;

/// <summary>
/// ClosedXML-backed implementation of ISpreadsheetReader. Each call opens the
/// workbook fresh from disk so the reader is stateless and safe to register as
/// a singleton.
/// </summary>
public class SpreadsheetReader : ISpreadsheetReader
{
    private const int DefaultRowLimit = 1000;

    public Result<SpreadsheetWorkbookInfo> GetInfo(string workbookPath)
    {
        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            var sheets = new List<SpreadsheetSheetInfo>();
            foreach (var worksheet in workbook.Worksheets)
            {
                var usedRange = worksheet.RangeUsed();
                string? usedRangeAddress = null;
                int rowCount = 0;
                int columnCount = 0;
                if (usedRange is not null)
                {
                    var rangeAddress = usedRange.RangeAddress;
                    usedRangeAddress = FormatA1Range(rangeAddress);
                    rowCount = rangeAddress.RowSpan;
                    columnCount = rangeAddress.ColumnSpan;
                }

                sheets.Add(new SpreadsheetSheetInfo(
                    worksheet.Name,
                    worksheet.Position,
                    usedRangeAddress,
                    rowCount,
                    columnCount,
                    worksheet.SheetView.SplitRow,
                    worksheet.SheetView.SplitColumn));
            }

            var namedRanges = new List<SpreadsheetNamedRange>();
            foreach (var definedName in workbook.DefinedNames)
            {
                namedRanges.Add(new SpreadsheetNamedRange(
                    definedName.Name,
                    definedName.RefersTo,
                    "workbook"));
            }
            foreach (var worksheet in workbook.Worksheets)
            {
                foreach (var definedName in worksheet.DefinedNames)
                {
                    namedRanges.Add(new SpreadsheetNamedRange(
                        definedName.Name,
                        definedName.RefersTo,
                        worksheet.Name));
                }
            }

            var info = new SpreadsheetWorkbookInfo(sheets, namedRanges);
            return Result<SpreadsheetWorkbookInfo>.Ok(info);
        }
        catch (Exception ex)
        {
            return Result<SpreadsheetWorkbookInfo>.Fail($"Failed to read workbook info from '{workbookPath}'")
                .WithException(ex);
        }
    }

    public Result<SpreadsheetReadResult> ReadSheet(string workbookPath, string sheetName, SpreadsheetReadOptions options)
    {
        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            var worksheetResult = GetWorksheet(workbook, sheetName);
            if (worksheetResult.IsFailure)
            {
                return Result<SpreadsheetReadResult>.Fail(worksheetResult.FirstErrorMessage);
            }
            var worksheet = worksheetResult.Value;

            var rangeResult = ResolveRange(worksheet, options.Range);
            if (rangeResult.IsFailure)
            {
                return Result<SpreadsheetReadResult>.Fail(rangeResult.FirstErrorMessage);
            }
            var range = rangeResult.Value.Range;

            if (range is null)
            {
                var emptyResult = new SpreadsheetReadResult(
                    Array.Empty<object?>(),
                    0,
                    Array.Empty<string>());
                return Result<SpreadsheetReadResult>.Ok(emptyResult);
            }

            return ReadRange(range, options);
        }
        catch (Exception ex)
        {
            return Result<SpreadsheetReadResult>.Fail($"Failed to read sheet '{sheetName}' from '{workbookPath}'")
                .WithException(ex);
        }
    }

    public Result<SpreadsheetExportCsvResult> ExportCsv(string workbookPath, string sheetName, string? range)
    {
        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            var worksheetResult = GetWorksheet(workbook, sheetName);
            if (worksheetResult.IsFailure)
            {
                return Result<SpreadsheetExportCsvResult>.Fail(worksheetResult.FirstErrorMessage);
            }
            var worksheet = worksheetResult.Value;

            var rangeResult = ResolveRange(worksheet, range);
            if (rangeResult.IsFailure)
            {
                return Result<SpreadsheetExportCsvResult>.Fail(rangeResult.FirstErrorMessage);
            }
            var resolvedRange = rangeResult.Value.Range;

            if (resolvedRange is null)
            {
                var emptyResult = new SpreadsheetExportCsvResult(string.Empty, 0, 0);
                return Result<SpreadsheetExportCsvResult>.Ok(emptyResult);
            }

            var builder = new StringBuilder();
            var rowCount = resolvedRange.RangeAddress.RowSpan;
            var columnCount = resolvedRange.RangeAddress.ColumnSpan;

            for (int rowOffset = 1; rowOffset <= rowCount; rowOffset++)
            {
                var rangeRow = resolvedRange.Row(rowOffset);
                for (int columnOffset = 1; columnOffset <= columnCount; columnOffset++)
                {
                    if (columnOffset > 1)
                    {
                        builder.Append(',');
                    }
                    var cell = rangeRow.Cell(columnOffset);
                    var cellValue = SpreadsheetValueConverter.ToJsonValue(cell.Value);
                    builder.Append(SpreadsheetValueConverter.ToCsvField(cellValue));
                }
                builder.Append("\r\n");
            }

            var csvResult = new SpreadsheetExportCsvResult(builder.ToString(), rowCount, columnCount);
            return Result<SpreadsheetExportCsvResult>.Ok(csvResult);
        }
        catch (Exception ex)
        {
            return Result<SpreadsheetExportCsvResult>.Fail($"Failed to export sheet '{sheetName}' as CSV from '{workbookPath}'")
                .WithException(ex);
        }
    }

    public Result<SpreadsheetActiveView> GetActiveView(string workbookPath)
    {
        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            IXLWorksheet? activeWorksheet = null;
            foreach (var worksheet in workbook.Worksheets)
            {
                if (worksheet.TabActive)
                {
                    activeWorksheet = worksheet;
                    break;
                }
            }
            if (activeWorksheet is null)
            {
                activeWorksheet = workbook.Worksheets.FirstOrDefault();
            }
            if (activeWorksheet is null)
            {
                return Result<SpreadsheetActiveView>.Fail($"Workbook '{workbookPath}' has no worksheets.");
            }

            string activeCellString;
            var activeCell = activeWorksheet.ActiveCell;
            var selectedRanges = activeWorksheet.SelectedRanges;
            if (activeCell is not null)
            {
                activeCellString = activeCell.Address.ToStringRelative();
            }
            else if (selectedRanges.Count > 0)
            {
                activeCellString = selectedRanges.First().RangeAddress.FirstAddress.ToStringRelative();
            }
            else
            {
                activeCellString = "A1";
            }

            // OOXML stores the selection as an active cell plus a list of ranges
            // (the sqref attribute, space-separated). On reload ClosedXML inserts
            // a degenerate single-cell range equal to the active cell whenever
            // the selection has more than one cell or more than one range. Filter
            // those out so the returned ranges match what the user (or a previous
            // set call) actually specified. Keep at least one range so the
            // returned list is never empty.
            var rangeStrings = new List<string>();
            foreach (var selected in selectedRanges)
            {
                var formatted = FormatA1RangeOrCell(selected.RangeAddress);
                if (selectedRanges.Count > 1
                    && IsSingleCellRangeMatchingAddress(selected.RangeAddress, activeCellString))
                {
                    continue;
                }
                rangeStrings.Add(formatted);
            }
            if (rangeStrings.Count == 0)
            {
                rangeStrings.Add("A1");
            }
            var rangeString = rangeStrings[0];

            // ClosedXML omits the topLeftCell attribute from OOXML when the value
            // equals the A1 default, then on reload returns a zeroed address whose
            // ToStringRelative() is "#REF!". Treat any address with non-positive
            // row or column as "scrolled to A1" so the round trip is stable.
            string topLeftCellString;
            var topLeftAddress = activeWorksheet.SheetView.TopLeftCellAddress;
            if (topLeftAddress is not null
                && topLeftAddress.RowNumber > 0
                && topLeftAddress.ColumnNumber > 0)
            {
                topLeftCellString = topLeftAddress.ToStringRelative();
            }
            else
            {
                topLeftCellString = "A1";
            }

            var view = new SpreadsheetActiveView(
                activeWorksheet.Name,
                rangeString,
                rangeStrings,
                activeCellString,
                topLeftCellString);

            return Result<SpreadsheetActiveView>.Ok(view);
        }
        catch (Exception ex)
        {
            return Result<SpreadsheetActiveView>.Fail($"Failed to read active view from '{workbookPath}'")
                .WithException(ex);
        }
    }

    public Result<SpreadsheetFindResult> Find(string workbookPath, SpreadsheetFindOptions options)
    {
        if (string.IsNullOrEmpty(options.Find))
        {
            return Result<SpreadsheetFindResult>.Fail("Find text is required and must be non-empty.");
        }
        if (!string.IsNullOrEmpty(options.Range)
            && options.Range.Contains('!'))
        {
            return Result<SpreadsheetFindResult>.Fail($"Range '{options.Range}' must not include a sheet qualifier.");
        }
        if (!string.IsNullOrEmpty(options.Range)
            && string.IsNullOrEmpty(options.Sheet))
        {
            return Result<SpreadsheetFindResult>.Fail("Range can only be used together with a specific sheet.");
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            IEnumerable<IXLWorksheet> targetSheets;
            if (string.IsNullOrEmpty(options.Sheet))
            {
                targetSheets = workbook.Worksheets;
            }
            else
            {
                if (!workbook.Worksheets.Contains(options.Sheet))
                {
                    return Result<SpreadsheetFindResult>.Fail($"Sheet not found: '{options.Sheet}'.");
                }
                targetSheets = new[] { workbook.Worksheet(options.Sheet) };
            }

            var matches = new List<SpreadsheetFindMatch>();
            var comparison = options.MatchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            foreach (var worksheet in targetSheets)
            {
                var cellsResult = ResolveSearchCells(worksheet, options.Range);
                if (cellsResult.IsFailure)
                {
                    return Result<SpreadsheetFindResult>.Fail(cellsResult.FirstErrorMessage);
                }

                foreach (var cell in cellsResult.Value)
                {
                    string cellText;
                    bool isFormula;
                    if (cell.HasFormula)
                    {
                        cellText = cell.FormulaA1;
                        isFormula = true;
                    }
                    else if (cell.Value.IsText)
                    {
                        cellText = cell.GetString();
                        isFormula = false;
                    }
                    else
                    {
                        continue;
                    }

                    if (!IsMatch(cellText, options, comparison))
                    {
                        continue;
                    }

                    matches.Add(new SpreadsheetFindMatch(
                        worksheet.Name,
                        cell.Address.ToStringRelative(),
                        cellText,
                        isFormula));
                }
            }

            return Result<SpreadsheetFindResult>.Ok(new SpreadsheetFindResult(matches, matches.Count));
        }
        catch (Exception ex)
        {
            return Result<SpreadsheetFindResult>.Fail($"Failed to search workbook '{workbookPath}'")
                .WithException(ex);
        }
    }

    private static Result<IEnumerable<IXLCell>> ResolveSearchCells(IXLWorksheet worksheet, string range)
    {
        if (string.IsNullOrEmpty(range))
        {
            return Result<IEnumerable<IXLCell>>.Ok(worksheet.CellsUsed(XLCellsUsedOptions.Contents));
        }

        try
        {
            if (SpreadsheetCommandHelpers.IsRowRange(range))
            {
                var rows = worksheet.Rows(range);
                return Result<IEnumerable<IXLCell>>.Ok(rows.SelectMany(r => r.CellsUsed(XLCellsUsedOptions.Contents)));
            }
            if (SpreadsheetCommandHelpers.IsColumnRange(range))
            {
                var columns = worksheet.Columns(range);
                return Result<IEnumerable<IXLCell>>.Ok(columns.SelectMany(c => c.CellsUsed(XLCellsUsedOptions.Contents)));
            }
            var xlRange = worksheet.Range(range);
            return Result<IEnumerable<IXLCell>>.Ok(xlRange.CellsUsed(XLCellsUsedOptions.Contents));
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<IXLCell>>.Fail($"Invalid range '{range}': {ex.Message}");
        }
    }

    private static bool IsMatch(string source, SpreadsheetFindOptions options, StringComparison comparison)
    {
        if (options.MatchEntireCellContents)
        {
            return string.Equals(source, options.Find, comparison);
        }
        return source.IndexOf(options.Find, comparison) >= 0;
    }

    public Result<SpreadsheetReadFormatResult> ReadFormat(string workbookPath, string sheetName, string? range)
    {
        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            var worksheetResult = GetWorksheet(workbook, sheetName);
            if (worksheetResult.IsFailure)
            {
                return Result<SpreadsheetReadFormatResult>.Fail(worksheetResult.FirstErrorMessage);
            }
            var worksheet = worksheetResult.Value;

            var rangeResult = ResolveRange(worksheet, range);
            if (rangeResult.IsFailure)
            {
                return Result<SpreadsheetReadFormatResult>.Fail(rangeResult.FirstErrorMessage);
            }
            var resolvedRange = rangeResult.Value.Range;

            if (resolvedRange is null)
            {
                var emptyResult = new SpreadsheetReadFormatResult(sheetName, new List<List<SpreadsheetFormatSpec>>());
                return Result<SpreadsheetReadFormatResult>.Ok(emptyResult);
            }

            var address = resolvedRange.RangeAddress;
            var rangeString = $"{sheetName}!{FormatA1Range(address)}";
            var rowCount = address.RowSpan;
            var columnCount = address.ColumnSpan;

            var rows = new List<List<SpreadsheetFormatSpec>>(rowCount);
            for (int rowOffset = 1; rowOffset <= rowCount; rowOffset++)
            {
                var rowSpecs = new List<SpreadsheetFormatSpec>(columnCount);
                for (int columnOffset = 1; columnOffset <= columnCount; columnOffset++)
                {
                    var cell = resolvedRange.Cell(rowOffset, columnOffset);
                    var spec = SpreadsheetFormatReader.ReadFormatFromCell(cell);
                    rowSpecs.Add(spec);
                }
                rows.Add(rowSpecs);
            }

            var result = new SpreadsheetReadFormatResult(rangeString, rows);
            return Result<SpreadsheetReadFormatResult>.Ok(result);
        }
        catch (Exception ex)
        {
            return Result<SpreadsheetReadFormatResult>.Fail(
                $"Failed to read format from sheet '{sheetName}' in '{workbookPath}'")
                .WithException(ex);
        }
    }

    private record ResolvedRange(IXLRange? Range);

    private static Result<IXLWorksheet> GetWorksheet(XLWorkbook workbook, string sheetName)
    {
        if (!workbook.Worksheets.Contains(sheetName))
        {
            return Result<IXLWorksheet>.Fail($"Sheet not found: '{sheetName}'");
        }

        var worksheet = workbook.Worksheet(sheetName);
        return Result<IXLWorksheet>.Ok(worksheet);
    }

    private static Result<ResolvedRange> ResolveRange(IXLWorksheet worksheet, string? requestedRange)
    {
        if (string.IsNullOrEmpty(requestedRange))
        {
            var usedRange = worksheet.RangeUsed();
            return Result<ResolvedRange>.Ok(new ResolvedRange(usedRange));
        }

        if (requestedRange.Contains('!'))
        {
            return Result<ResolvedRange>.Fail(
                $"Range must not include a sheet qualifier: '{requestedRange}'. " +
                "Pass the sheet name as a separate parameter and the range as plain A1 notation (e.g. 'B2:D10').");
        }

        try
        {
            var range = worksheet.Range(requestedRange);
            return Result<ResolvedRange>.Ok(new ResolvedRange(range));
        }
        catch (Exception ex)
        {
            return Result<ResolvedRange>.Fail($"Invalid range '{requestedRange}': {ex.Message}");
        }
    }

    private static Result<SpreadsheetReadResult> ReadRange(IXLRange range, SpreadsheetReadOptions options)
    {
        var totalRows = range.RangeAddress.RowSpan;
        var totalColumns = range.RangeAddress.ColumnSpan;

        if (options.Headers)
        {
            return ReadRangeWithHeaders(range, options, totalRows, totalColumns);
        }

        return ReadRangeAsArrays(range, options, totalRows, totalColumns);
    }

    private static Result<SpreadsheetReadResult> ReadRangeAsArrays(
        IXLRange range,
        SpreadsheetReadOptions options,
        int totalRows,
        int totalColumns)
    {
        var dataRowCount = totalRows;
        var startRowOffset = options.Offset > 0 ? options.Offset : 0;
        var limit = ResolveLimit(options.Limit);
        var endRowOffset = Math.Min(dataRowCount, startRowOffset + limit);

        var rows = new List<object?>();
        for (int rowIndex = startRowOffset; rowIndex < endRowOffset; rowIndex++)
        {
            var rangeRow = range.Row(rowIndex + 1);
            var rowValues = new object?[totalColumns];
            for (int columnIndex = 0; columnIndex < totalColumns; columnIndex++)
            {
                var cell = rangeRow.Cell(columnIndex + 1);
                rowValues[columnIndex] = ReadCellValue(cell, options.Mode);
            }
            rows.Add(rowValues);
        }

        var result = new SpreadsheetReadResult(rows, dataRowCount, Array.Empty<string>());
        return Result<SpreadsheetReadResult>.Ok(result);
    }

    private static Result<SpreadsheetReadResult> ReadRangeWithHeaders(
        IXLRange range,
        SpreadsheetReadOptions options,
        int totalRows,
        int totalColumns)
    {
        if (totalRows < 1)
        {
            var empty = new SpreadsheetReadResult(
                Array.Empty<object?>(),
                0,
                Array.Empty<string>());
            return Result<SpreadsheetReadResult>.Ok(empty);
        }

        var headerRow = range.Row(1);
        var headers = ResolveHeaders(headerRow, totalColumns);

        var dataRowCount = totalRows - 1;
        var startRowOffset = options.Offset > 0 ? options.Offset : 0;
        var limit = ResolveLimit(options.Limit);
        var endRowOffset = Math.Min(dataRowCount, startRowOffset + limit);

        var rows = new List<object?>();
        for (int dataRowIndex = startRowOffset; dataRowIndex < endRowOffset; dataRowIndex++)
        {
            var rangeRow = range.Row(dataRowIndex + 2);
            var rowDictionary = new Dictionary<string, object?>(totalColumns);
            for (int columnIndex = 0; columnIndex < totalColumns; columnIndex++)
            {
                var cell = rangeRow.Cell(columnIndex + 1);
                var cellValue = ReadCellValue(cell, options.Mode);
                rowDictionary[headers[columnIndex]] = cellValue;
            }
            rows.Add(rowDictionary);
        }

        var result = new SpreadsheetReadResult(rows, dataRowCount, headers);
        return Result<SpreadsheetReadResult>.Ok(result);
    }

    private static IReadOnlyList<string> ResolveHeaders(IXLRangeRow headerRow, int columnCount)
    {
        var headers = new string[columnCount];
        var seenNames = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var cell = headerRow.Cell(columnIndex + 1);
            string headerName;

            if (cell.Value.IsBlank)
            {
                var columnLetter = XLHelper.GetColumnLetterFromNumber(cell.Address.ColumnNumber, false);
                headerName = $"column_{columnLetter}";
            }
            else
            {
                headerName = cell.GetString();
            }

            if (seenNames.TryGetValue(headerName, out var occurrenceCount))
            {
                occurrenceCount++;
                seenNames[headerName] = occurrenceCount;
                headers[columnIndex] = $"{headerName}_{occurrenceCount}";
            }
            else
            {
                seenNames[headerName] = 1;
                headers[columnIndex] = headerName;
            }
        }

        return headers;
    }

    private static object? ReadCellValue(IXLCell cell, SpreadsheetReadMode mode)
    {
        if (mode == SpreadsheetReadMode.Formulas && cell.HasFormula)
        {
            return "=" + cell.FormulaA1;
        }

        return SpreadsheetValueConverter.ToJsonValue(cell.Value);
    }

    private static int ResolveLimit(int limit)
    {
        if (limit > 0)
        {
            return limit;
        }

        return DefaultRowLimit;
    }

    private static string FormatA1Range(IXLRangeAddress rangeAddress)
    {
        var firstColumn = XLHelper.GetColumnLetterFromNumber(rangeAddress.FirstAddress.ColumnNumber, false);
        var firstRow = rangeAddress.FirstAddress.RowNumber;
        var lastColumn = XLHelper.GetColumnLetterFromNumber(rangeAddress.LastAddress.ColumnNumber, false);
        var lastRow = rangeAddress.LastAddress.RowNumber;
        return $"{firstColumn}{firstRow}:{lastColumn}{lastRow}";
    }

    private static string FormatA1RangeOrCell(IXLRangeAddress rangeAddress)
    {
        var firstColumn = XLHelper.GetColumnLetterFromNumber(rangeAddress.FirstAddress.ColumnNumber, false);
        var firstRow = rangeAddress.FirstAddress.RowNumber;
        var lastColumn = XLHelper.GetColumnLetterFromNumber(rangeAddress.LastAddress.ColumnNumber, false);
        var lastRow = rangeAddress.LastAddress.RowNumber;
        if (firstColumn == lastColumn
            && firstRow == lastRow)
        {
            return $"{firstColumn}{firstRow}";
        }
        return $"{firstColumn}{firstRow}:{lastColumn}{lastRow}";
    }

    private static bool IsSingleCellRangeMatchingAddress(IXLRangeAddress rangeAddress, string activeCellAddress)
    {
        if (rangeAddress.FirstAddress.RowNumber != rangeAddress.LastAddress.RowNumber
            || rangeAddress.FirstAddress.ColumnNumber != rangeAddress.LastAddress.ColumnNumber)
        {
            return false;
        }

        var firstColumn = XLHelper.GetColumnLetterFromNumber(rangeAddress.FirstAddress.ColumnNumber, false);
        var firstRow = rangeAddress.FirstAddress.RowNumber;
        var formatted = $"{firstColumn}{firstRow}";
        return string.Equals(formatted, activeCellAddress, StringComparison.OrdinalIgnoreCase);
    }
}
