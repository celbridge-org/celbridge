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
