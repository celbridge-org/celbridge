using Celbridge.Commands;
using Celbridge.Spreadsheet.Helpers;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class ClearRangesCommand : CommandBase, IClearRangesCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public IReadOnlyList<ClearRangesOperation> Operations { get; set; } = Array.Empty<ClearRangesOperation>();

    public ClearRangesResult ResultValue { get; private set; } =
        new ClearRangesResult(0, 0);

    public ClearRangesCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resolveResult = await SpreadsheetHelper.ResolveWorkbookResourceAsync(_workspaceWrapper, FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult.FirstErrorMessage);
        }
        var workbookResource = resolveResult.Value;

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

        var fileSystem = _workspaceWrapper.WorkspaceService.ResourceFileSystem;
        var loadResult = await SpreadsheetHelper.LoadWorkbookAsync(fileSystem, workbookResource);
        if (loadResult.IsFailure)
        {
            return Result.Fail(loadResult);
        }

        try
        {
            using var workbook = loadResult.Value;

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

            var saveResult = await SpreadsheetHelper.SaveWorkbookAsync(fileSystem, workbookResource, workbook);
            if (saveResult.IsFailure)
            {
                return Result.Fail(saveResult);
            }

            ResultValue = new ClearRangesResult(Operations.Count, totalCellCount);
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

        if (SpreadsheetHelper.IsRowRange(range))
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

        if (SpreadsheetHelper.IsColumnRange(range))
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
