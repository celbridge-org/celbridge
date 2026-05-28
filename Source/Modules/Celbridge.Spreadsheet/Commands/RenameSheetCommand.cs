using Celbridge.Commands;
using Celbridge.Spreadsheet.Helpers;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class RenameSheetCommand : CommandBase, IRenameSheetCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;

    public RenameSheetResult ResultValue { get; private set; } =
        new RenameSheetResult(string.Empty, string.Empty);

    public RenameSheetCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (string.IsNullOrEmpty(NewName))
        {
            return Result.Fail("New sheet name is required.");
        }

        if (string.Equals(Sheet, NewName, StringComparison.Ordinal))
        {
            ResultValue = new RenameSheetResult(Sheet, NewName);
            return Result.Ok();
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
                return Result.Fail($"Sheet not found: '{Sheet}'.");
            }

            if (workbook.Worksheets.Contains(NewName))
            {
                return Result.Fail($"Cannot rename to '{NewName}': a sheet with that name already exists.");
            }

            var worksheet = workbook.Worksheet(Sheet);
            worksheet.Name = NewName;
            var saveResult = await SpreadsheetHelper.SaveWorkbookAsync(fileSystem, workbookResource, workbook);
            if (saveResult.IsFailure)
            {
                return Result.Fail(saveResult);
            }

            ResultValue = new RenameSheetResult(Sheet, NewName);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to rename sheet '{Sheet}' in '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
