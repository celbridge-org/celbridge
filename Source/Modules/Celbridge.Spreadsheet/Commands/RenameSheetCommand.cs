using Celbridge.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class RenameSheetCommand : CommandBase, ISpreadsheetRenameSheetCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;

    public RenameSheetCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (string.IsNullOrEmpty(NewName))
        {
            return Result.Fail("New sheet name is required.");
        }

        if (string.Equals(Sheet, NewName, StringComparison.Ordinal))
        {
            return Result.Ok();
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            if (!workbook.Worksheets.Contains(Sheet))
            {
                return Result.Fail($"Sheet not found: '{Sheet}'.");
            }

            if (workbook.Worksheets.Contains(NewName))
            {
                return Result.Fail($"Cannot rename to '{NewName}': a sheet with that name already exists.");
            }

            var worksheet = workbook.Worksheet(Sheet);
            worksheet.Name = NewName;
            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to rename sheet '{Sheet}' in '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
