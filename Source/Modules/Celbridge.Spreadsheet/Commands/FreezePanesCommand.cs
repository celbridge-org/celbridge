using Celbridge.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class FreezePanesCommand : CommandBase, IFreezePanesCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Columns { get; set; }

    public FreezePanesResult ResultValue { get; private set; } =
        new FreezePanesResult(string.Empty, 0, 0);

    public FreezePanesCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (Rows < 0 || Columns < 0)
        {
            return Result.Fail("Rows and Columns must be non-negative.");
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            if (!workbook.Worksheets.Contains(Sheet))
            {
                return Result.Fail($"Sheet not found: '{Sheet}'.");
            }
            var worksheet = workbook.Worksheet(Sheet);

            // Freezing more rows or columns than fit in a viewport produces a
            // degenerate file (everything frozen, nothing scrollable). Bound
            // by the sheet's used range, with a small floor so empty sheets
            // can still freeze a header row before data is added.
            const int EmptySheetFreezeFloor = 10;
            var usedRange = worksheet.RangeUsed();
            var usedRowCount = usedRange?.RangeAddress.RowSpan ?? 0;
            var usedColumnCount = usedRange?.RangeAddress.ColumnSpan ?? 0;
            var maxRows = Math.Max(usedRowCount, EmptySheetFreezeFloor);
            var maxColumns = Math.Max(usedColumnCount, EmptySheetFreezeFloor);

            if (Rows > maxRows)
            {
                return Result.Fail($"Rows {Rows} exceeds the sheet's bound of {maxRows} (the larger of used row count {usedRowCount} and floor {EmptySheetFreezeFloor}). Add data first or pass a smaller value.");
            }

            if (Columns > maxColumns)
            {
                return Result.Fail($"Columns {Columns} exceeds the sheet's bound of {maxColumns} (the larger of used column count {usedColumnCount} and floor {EmptySheetFreezeFloor}). Add data first or pass a smaller value.");
            }

            worksheet.SheetView.FreezeRows(Rows);
            worksheet.SheetView.FreezeColumns(Columns);

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            ResultValue = new FreezePanesResult(Sheet, Rows, Columns);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to freeze panes for '{Sheet}' in '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
