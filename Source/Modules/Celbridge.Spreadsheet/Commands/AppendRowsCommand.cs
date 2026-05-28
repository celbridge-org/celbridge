using Celbridge.Commands;
using Celbridge.Spreadsheet.Helpers;
using Celbridge.Spreadsheet.Services;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class AppendRowsCommand : CommandBase, IAppendRowsCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; set; } = Array.Empty<IReadOnlyList<object?>>();

    public AppendRowsResult ResultValue { get; private set; } = new AppendRowsResult(0, 0, 0);

    public AppendRowsCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (string.IsNullOrEmpty(Sheet))
        {
            return Result.Fail("Sheet name is required.");
        }

        if (Rows.Count == 0)
        {
            return Result.Fail("At least one row is required.");
        }

        // Reject ragged rows so append_rows matches import_csv's contract.
        // Pad-with-null behaviour is hostile to round-tripping: a column
        // ends up with mixed string/null cells whenever the writer omitted
        // a trailing field.
        var firstRowFieldCount = Rows[0].Count;
        for (int rowIndex = 1; rowIndex < Rows.Count; rowIndex++)
        {
            var rowFieldCount = Rows[rowIndex].Count;
            if (rowFieldCount != firstRowFieldCount)
            {
                return Result.Fail($"Row {rowIndex + 1} has {rowFieldCount} fields, expected {firstRowFieldCount}.");
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

            if (!workbook.Worksheets.Contains(Sheet))
            {
                return Result.Fail($"Sheet not found: '{Sheet}'. Add it via spreadsheet_add_sheets first.");
            }
            var worksheet = workbook.Worksheet(Sheet);

            var usedRange = worksheet.RangeUsed();
            int firstRow;
            if (usedRange is null)
            {
                firstRow = 1;
            }
            else
            {
                firstRow = usedRange.RangeAddress.LastAddress.RowNumber + 1;
            }

            for (int rowOffset = 0; rowOffset < Rows.Count; rowOffset++)
            {
                var rowValues = Rows[rowOffset];
                var rowNumber = firstRow + rowOffset;
                for (int columnIndex = 0; columnIndex < rowValues.Count; columnIndex++)
                {
                    var rawValue = rowValues[columnIndex];
                    if (rawValue is double doubleValue)
                    {
                        var validation = SpreadsheetHelper.ValidateNumericValue(doubleValue);
                        if (validation.IsFailure)
                        {
                            return Result.Fail($"Row {rowOffset + 1}, column {columnIndex + 1}: {validation.FirstErrorMessage}");
                        }
                    }
                    var cell = worksheet.Cell(rowNumber, columnIndex + 1);
                    ValueConverterHelper.SetCellValue(cell, rawValue);
                }
            }

            var saveResult = await SpreadsheetHelper.SaveWorkbookAsync(fileSystem, workbookResource, workbook);
            if (saveResult.IsFailure)
            {
                return Result.Fail(saveResult);
            }

            var lastRow = firstRow + Rows.Count - 1;
            ResultValue = new AppendRowsResult(Rows.Count, firstRow, lastRow);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to append rows to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
