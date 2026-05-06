using Celbridge.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class InsertRangesCommand : CommandBase, IInsertRangesCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public IReadOnlyList<SpreadsheetInsertRangesOperation> Operations { get; set; } = Array.Empty<SpreadsheetInsertRangesOperation>();

    public SpreadsheetInsertRangesResult ResultValue { get; private set; } =
        new SpreadsheetInsertRangesResult(0, 0, 0);

    public InsertRangesCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        var resolveResult = SpreadsheetCommandHelpers.ResolveWorkbookPath(_workspaceWrapper, FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult.FirstErrorMessage);
        }
        var workbookPath = resolveResult.Value;

        if (Operations.Count == 0)
        {
            return Result.Fail("At least one insert operation is required.");
        }

        for (int operationIndex = 0; operationIndex < Operations.Count; operationIndex++)
        {
            var operation = Operations[operationIndex];
            if (string.IsNullOrEmpty(operation.Sheet))
            {
                return Result.Fail($"Operation {operationIndex + 1}: sheet name is required.");
            }
            if (string.IsNullOrEmpty(operation.Range))
            {
                return Result.Fail($"Operation {operationIndex + 1}: range is required.");
            }
            if (operation.Range.Contains('!'))
            {
                return Result.Fail($"Operation {operationIndex + 1}: range '{operation.Range}' must not include a sheet qualifier.");
            }
        }

        // Group operations by sheet, then by axis. Each operation contributes
        // a (start, count) pair in original coordinates. We sort each bucket by
        // start in descending order so that earlier (higher-positioned) inserts
        // do not shift the positions of later ones — which keeps the
        // original-coordinate semantics. Within the same start, the order does
        // not matter: their inserts stack at that position.
        var rowsBySheet = new Dictionary<string, List<AxisRange>>(StringComparer.Ordinal);
        var columnsBySheet = new Dictionary<string, List<AxisRange>>(StringComparer.Ordinal);

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            for (int operationIndex = 0; operationIndex < Operations.Count; operationIndex++)
            {
                var operation = Operations[operationIndex];

                if (!workbook.Worksheets.Contains(operation.Sheet))
                {
                    return Result.Fail($"Operation {operationIndex + 1}: sheet not found: '{operation.Sheet}'.");
                }

                var resolveAxisResult = ResolveAxisRange(operation.Range);
                if (resolveAxisResult.IsFailure)
                {
                    return Result.Fail($"Operation {operationIndex + 1} ('{operation.Sheet}!{operation.Range}'): {resolveAxisResult.FirstErrorMessage}");
                }
                var axisRange = resolveAxisResult.Value;

                Dictionary<string, List<AxisRange>> bucket = axisRange.IsRows ? rowsBySheet : columnsBySheet;
                if (!bucket.TryGetValue(operation.Sheet, out var rangeList))
                {
                    rangeList = new List<AxisRange>();
                    bucket[operation.Sheet] = rangeList;
                }
                rangeList.Add(axisRange);
            }

            int totalRowsInserted = 0;
            int totalColumnsInserted = 0;

            foreach (var (sheetName, rowRanges) in rowsBySheet)
            {
                var worksheet = workbook.Worksheet(sheetName);
                foreach (var range in rowRanges.OrderByDescending(r => r.Start))
                {
                    worksheet.Row(range.Start).InsertRowsAbove(range.Count);
                    totalRowsInserted += range.Count;
                }
            }

            foreach (var (sheetName, columnRanges) in columnsBySheet)
            {
                var worksheet = workbook.Worksheet(sheetName);
                foreach (var range in columnRanges.OrderByDescending(r => r.Start))
                {
                    worksheet.Column(range.Start).InsertColumnsBefore(range.Count);
                    totalColumnsInserted += range.Count;
                }
            }

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            ResultValue = new SpreadsheetInsertRangesResult(Operations.Count, totalRowsInserted, totalColumnsInserted);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to apply insert operations to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }

    private record AxisRange(bool IsRows, int Start, int Count);

    private static Result<AxisRange> ResolveAxisRange(string range)
    {
        if (SpreadsheetCommandHelpers.IsRowRange(range))
        {
            try
            {
                var parts = range.Split(':');
                int firstRow = int.Parse(parts[0]);
                int lastRow = parts.Length > 1 ? int.Parse(parts[1]) : firstRow;
                if (firstRow < 1
                    || lastRow < firstRow)
                {
                    return Result.Fail($"Row range '{range}' is invalid.");
                }
                int count = lastRow - firstRow + 1;
                return new AxisRange(true, firstRow, count);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Invalid row range '{range}': {ex.Message}");
            }
        }

        if (SpreadsheetCommandHelpers.IsColumnRange(range))
        {
            try
            {
                var parts = range.Split(':');
                int firstColumn = XLHelper.GetColumnNumberFromLetter(parts[0]);
                int lastColumn = parts.Length > 1 ? XLHelper.GetColumnNumberFromLetter(parts[1]) : firstColumn;
                if (firstColumn < 1
                    || lastColumn < firstColumn)
                {
                    return Result.Fail($"Column range '{range}' is invalid.");
                }
                int count = lastColumn - firstColumn + 1;
                return new AxisRange(false, firstColumn, count);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Invalid column range '{range}': {ex.Message}");
            }
        }

        return Result.Fail(
            $"Range '{range}' is not a row or column range. Use \"3\" or \"3:5\" for rows, \"B\" or \"B:D\" for columns. Cell ranges (e.g. \"A1:C3\") are not accepted by insert.");
    }
}
