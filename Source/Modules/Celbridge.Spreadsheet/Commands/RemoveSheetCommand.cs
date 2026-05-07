using Celbridge.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class RemoveSheetCommand : CommandBase, IRemoveSheetCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;

    public RemoveSheetResult ResultValue { get; private set; } =
        new RemoveSheetResult(string.Empty);

    public RemoveSheetCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        var resolveResult = SpreadsheetHelper.ResolveWorkbookPath(_workspaceWrapper, FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult.FirstErrorMessage);
        }
        var workbookPath = resolveResult.Value;

        if (string.IsNullOrEmpty(Sheet))
        {
            return Result.Fail("Sheet name is required.");
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            if (!workbook.Worksheets.Contains(Sheet))
            {
                return Result.Fail($"Sheet not found: '{Sheet}'.");
            }

            if (workbook.Worksheets.Count == 1)
            {
                return Result.Fail($"Cannot remove '{Sheet}': a workbook must contain at least one sheet.");
            }

            workbook.Worksheets.Delete(Sheet);
            SpreadsheetHelper.RecalculateAndSave(workbook);

            ResultValue = new RemoveSheetResult(Sheet);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to remove sheet '{Sheet}' from '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
