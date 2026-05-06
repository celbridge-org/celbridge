using Celbridge.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class ClearCommand : CommandBase, ISpreadsheetClearCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public IReadOnlyList<SpreadsheetClearOperation> Operations { get; set; } = Array.Empty<SpreadsheetClearOperation>();

    public SpreadsheetClearResult ResultValue { get; private set; } =
        new SpreadsheetClearResult(0, 0);

    public ClearCommand(IWorkspaceWrapper workspaceWrapper)
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
            return Result.Fail("At least one clear operation is required.");
        }

        for (int operationIndex = 0; operationIndex < Operations.Count; operationIndex++)
        {
            var operation = Operations[operationIndex];
            if (string.IsNullOrEmpty(operation.Sheet))
            {
                return Result.Fail($"Operation {operationIndex + 1}: sheet name is required.");
            }
            if (!string.IsNullOrEmpty(operation.Range)
                && operation.Range.Contains('!'))
            {
                return Result.Fail($"Operation {operationIndex + 1}: range '{operation.Range}' must not include a sheet qualifier.");
            }
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            int totalCellCount = 0;

            for (int operationIndex = 0; operationIndex < Operations.Count; operationIndex++)
            {
                var operation = Operations[operationIndex];

                if (!workbook.Worksheets.Contains(operation.Sheet))
                {
                    return Result.Fail($"Operation {operationIndex + 1}: sheet not found: '{operation.Sheet}'.");
                }
                var worksheet = workbook.Worksheet(operation.Sheet);

                var clearResult = ApplyClear(worksheet, operation.Range, out int cellCount);
                if (clearResult.IsFailure)
                {
                    return Result.Fail($"Operation {operationIndex + 1} ('{operation.Sheet}!{operation.Range}'): {clearResult.FirstErrorMessage}");
                }

                totalCellCount += cellCount;
            }

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            ResultValue = new SpreadsheetClearResult(Operations.Count, totalCellCount);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to apply clear operations to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }

    private static Result ApplyClear(IXLWorksheet worksheet, string range, out int cellCount)
    {
        cellCount = 0;

        // XLCellsUsedOptions.All counts cells with only formatting, comments,
        // merged ranges or data validation alongside value cells. Capture the
        // count before clearing because Clear resets state to default.
        if (string.IsNullOrEmpty(range))
        {
            cellCount = worksheet.CellsUsed(XLCellsUsedOptions.All).Count();
            worksheet.Clear(XLClearOptions.All);
            return Result.Ok();
        }

        if (SpreadsheetCommandHelpers.IsRowRange(range))
        {
            try
            {
                var rows = worksheet.Rows(range).ToList();
                if (rows.Count == 0)
                {
                    return Result.Fail($"Row range '{range}' did not match any rows.");
                }
                foreach (var row in rows)
                {
                    cellCount += row.CellsUsed(XLCellsUsedOptions.All).Count();
                    row.Clear(XLClearOptions.All);
                }
                return Result.Ok();
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
                var columns = worksheet.Columns(range).ToList();
                if (columns.Count == 0)
                {
                    return Result.Fail($"Column range '{range}' did not match any columns.");
                }
                foreach (var column in columns)
                {
                    cellCount += column.CellsUsed(XLCellsUsedOptions.All).Count();
                    column.Clear(XLClearOptions.All);
                }
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Result.Fail($"Invalid column range '{range}': {ex.Message}");
            }
        }

        IXLRange xlRange;
        try
        {
            xlRange = worksheet.Range(range);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Invalid cell range '{range}': {ex.Message}");
        }

        cellCount = xlRange.CellsUsed(XLCellsUsedOptions.All).Count();
        xlRange.Clear(XLClearOptions.All);
        return Result.Ok();
    }
}
