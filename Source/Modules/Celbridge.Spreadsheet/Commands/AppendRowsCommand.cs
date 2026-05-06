using Celbridge.Commands;
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

    public SpreadsheetAppendRowsResult ResultValue { get; private set; } = new SpreadsheetAppendRowsResult(0, 0, 0);

    public AppendRowsCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (string.IsNullOrEmpty(Sheet))
        {
            return Result.Fail("Sheet name is required.");
        }

        if (Rows.Count == 0)
        {
            return Result.Fail("At least one row is required.");
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

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
                    var cell = worksheet.Cell(rowNumber, columnIndex + 1);
                    SpreadsheetValueConverter.SetCellValue(cell, rowValues[columnIndex]);
                }
            }

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            var lastRow = firstRow + Rows.Count - 1;
            ResultValue = new SpreadsheetAppendRowsResult(Rows.Count, firstRow, lastRow);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to append rows to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
