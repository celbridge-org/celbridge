using Celbridge.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class DeleteCommand : CommandBase, ISpreadsheetDeleteCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public IReadOnlyList<SpreadsheetDeleteOperation> Operations { get; set; } = Array.Empty<SpreadsheetDeleteOperation>();

    public SpreadsheetDeleteResult ResultValue { get; private set; } =
        new SpreadsheetDeleteResult(0, 0, 0);

    public DeleteCommand(IWorkspaceWrapper workspaceWrapper)
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
            return Result.Fail("At least one delete operation is required.");
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

        // Group operations by sheet, then by axis. Indices refer to the
        // original workbook state, so we collect the union per axis and
        // delete in descending order — earlier deletes do not shift later
        // ones, and overlapping ranges dedupe naturally.
        var rowsBySheet = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        var columnsBySheet = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);

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
                var worksheet = workbook.Worksheet(operation.Sheet);

                var resolveAxisResult = ResolveAxisIndices(worksheet, operation.Range);
                if (resolveAxisResult.IsFailure)
                {
                    return Result.Fail($"Operation {operationIndex + 1} ('{operation.Sheet}!{operation.Range}'): {resolveAxisResult.FirstErrorMessage}");
                }
                var axisIndices = resolveAxisResult.Value;

                Dictionary<string, SortedSet<int>> bucket = axisIndices.IsRows ? rowsBySheet : columnsBySheet;
                if (!bucket.TryGetValue(operation.Sheet, out var indexSet))
                {
                    indexSet = new SortedSet<int>();
                    bucket[operation.Sheet] = indexSet;
                }
                for (int index = axisIndices.Start; index <= axisIndices.End; index++)
                {
                    indexSet.Add(index);
                }
            }

            int totalRowsDeleted = 0;
            int totalColumnsDeleted = 0;

            foreach (var (sheetName, rowIndices) in rowsBySheet)
            {
                var worksheet = workbook.Worksheet(sheetName);
                foreach (var rowIndex in rowIndices.Reverse())
                {
                    worksheet.Row(rowIndex).Delete();
                    totalRowsDeleted++;
                }
            }

            foreach (var (sheetName, columnIndices) in columnsBySheet)
            {
                var worksheet = workbook.Worksheet(sheetName);
                foreach (var columnIndex in columnIndices.Reverse())
                {
                    worksheet.Column(columnIndex).Delete();
                    totalColumnsDeleted++;
                }
            }

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            ResultValue = new SpreadsheetDeleteResult(Operations.Count, totalRowsDeleted, totalColumnsDeleted);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to apply delete operations to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }

    private record AxisIndices(bool IsRows, int Start, int End);

    private static Result<AxisIndices> ResolveAxisIndices(IXLWorksheet worksheet, string range)
    {
        if (SpreadsheetCommandHelpers.IsRowRange(range))
        {
            try
            {
                var rows = worksheet.Rows(range).ToList();
                if (rows.Count == 0)
                {
                    return Result<AxisIndices>.Fail($"Row range '{range}' did not match any rows.");
                }
                var firstRow = rows.Min(r => r.RowNumber());
                var lastRow = rows.Max(r => r.RowNumber());

                return Result<AxisIndices>.Ok(new AxisIndices(true, firstRow, lastRow));
            }
            catch (Exception ex)
            {
                return Result<AxisIndices>.Fail($"Invalid row range '{range}': {ex.Message}");
            }
        }

        if (SpreadsheetCommandHelpers.IsColumnRange(range))
        {
            try
            {
                var columns = worksheet.Columns(range).ToList();
                if (columns.Count == 0)
                {
                    return Result<AxisIndices>.Fail($"Column range '{range}' did not match any columns.");
                }
                var firstColumn = columns.Min(c => c.ColumnNumber());
                var lastColumn = columns.Max(c => c.ColumnNumber());

                return Result<AxisIndices>.Ok(new AxisIndices(false, firstColumn, lastColumn));
            }
            catch (Exception ex)
            {
                return Result<AxisIndices>.Fail($"Invalid column range '{range}': {ex.Message}");
            }
        }

        return Result<AxisIndices>.Fail(
            $"Range '{range}' is not a row or column range. Use \"3\" or \"3:5\" for rows, \"B\" or \"B:D\" for columns. Cell ranges (e.g. \"A1:C3\") are not accepted by delete.");
    }
}
