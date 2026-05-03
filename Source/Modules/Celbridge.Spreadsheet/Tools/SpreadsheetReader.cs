using System.Text;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Tools;

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
                    usedRangeAddress,
                    rowCount,
                    columnCount));
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

    public Result<SpreadsheetCsvResult> ToCsv(string workbookPath, string sheetName, string? range)
    {
        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            var worksheetResult = GetWorksheet(workbook, sheetName);
            if (worksheetResult.IsFailure)
            {
                return Result<SpreadsheetCsvResult>.Fail(worksheetResult.FirstErrorMessage);
            }
            var worksheet = worksheetResult.Value;

            var rangeResult = ResolveRange(worksheet, range);
            if (rangeResult.IsFailure)
            {
                return Result<SpreadsheetCsvResult>.Fail(rangeResult.FirstErrorMessage);
            }
            var resolvedRange = rangeResult.Value.Range;

            if (resolvedRange is null)
            {
                var emptyResult = new SpreadsheetCsvResult(string.Empty, 0, 0);
                return Result<SpreadsheetCsvResult>.Ok(emptyResult);
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

            var csvResult = new SpreadsheetCsvResult(builder.ToString(), rowCount, columnCount);
            return Result<SpreadsheetCsvResult>.Ok(csvResult);
        }
        catch (Exception ex)
        {
            return Result<SpreadsheetCsvResult>.Fail($"Failed to export sheet '{sheetName}' as CSV from '{workbookPath}'")
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
}
