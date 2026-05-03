using Celbridge.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class AddSheetCommand : CommandBase, ISpreadsheetAddSheetCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;

    public AddSheetCommand(IWorkspaceWrapper workspaceWrapper)
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

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            if (workbook.Worksheets.Contains(Sheet))
            {
                return Result.Fail($"Sheet already exists: '{Sheet}'.");
            }

            workbook.Worksheets.Add(Sheet);
            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to add sheet '{Sheet}' to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
