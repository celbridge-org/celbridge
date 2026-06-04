using Celbridge.Commands;
using Celbridge.Workspace;

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
        var resolveResult = await SpreadsheetHelper.ResolveWorkbookResourceAsync(_workspaceWrapper, FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        if (string.IsNullOrEmpty(Sheet))
        {
            return Result.Fail("Sheet name is required.");
        }

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var loadResult = await SpreadsheetHelper.LoadWorkbookAsync(resourceFileSystem, workbookResource);
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

            if (workbook.Worksheets.Count == 1)
            {
                return Result.Fail($"Cannot remove '{Sheet}': a workbook must contain at least one sheet.");
            }

            workbook.Worksheets.Delete(Sheet);
            var saveResult = await SpreadsheetHelper.SaveWorkbookAsync(resourceFileSystem, workbookResource, workbook);
            if (saveResult.IsFailure)
            {
                return Result.Fail(saveResult);
            }

            ResultValue = new RemoveSheetResult(Sheet);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to remove sheet '{Sheet}' from '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
