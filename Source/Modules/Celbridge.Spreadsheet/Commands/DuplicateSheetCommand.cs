using Celbridge.Commands;
using Celbridge.Spreadsheet.Helpers;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class DuplicateSheetCommand : CommandBase, IDuplicateSheetCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string SourceSheet { get; set; } = string.Empty;
    public string NewSheet { get; set; } = string.Empty;
    public int Position { get; set; }

    public DuplicateSheetResult ResultValue { get; private set; } =
        new DuplicateSheetResult(string.Empty, 0);

    public DuplicateSheetCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (string.IsNullOrEmpty(SourceSheet))
        {
            return Result.Fail("Source sheet name is required.");
        }

        if (string.IsNullOrEmpty(NewSheet))
        {
            return Result.Fail("New sheet name is required.");
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

            if (!workbook.Worksheets.Contains(SourceSheet))
            {
                return Result.Fail($"Source sheet not found: '{SourceSheet}'.");
            }

            if (workbook.Worksheets.Contains(NewSheet))
            {
                return Result.Fail($"Sheet already exists: '{NewSheet}'.");
            }

            var sheetCount = workbook.Worksheets.Count;
            var maxPosition = sheetCount + 1;
            if (Position < 0 || Position > maxPosition)
            {
                return Result.Fail($"Position must be in [0, {maxPosition}], was {Position}. 0 appends after existing sheets.");
            }

            var sourceWorksheet = workbook.Worksheet(SourceSheet);

            // ClosedXML's IXLWorksheet.CopyTo throws a NullReferenceException
            // when the source has color-scale conditional formatting. Snapshot
            // and strip those rules first, then replay them onto both sheets
            // after the copy succeeds.
            var colorScaleSnapshots = ColorScaleCopyHelper.ExtractAndRemove(sourceWorksheet);

            IXLWorksheet duplicate;
            if (Position == 0)
            {
                duplicate = sourceWorksheet.CopyTo(NewSheet);
            }
            else
            {
                duplicate = sourceWorksheet.CopyTo(NewSheet, Position);
            }

            if (colorScaleSnapshots.Count > 0)
            {
                ColorScaleCopyHelper.Reapply(sourceWorksheet, colorScaleSnapshots);
                ColorScaleCopyHelper.Reapply(duplicate, colorScaleSnapshots);
            }

            var saveResult = await SpreadsheetHelper.SaveWorkbookAsync(resourceFileSystem, workbookResource, workbook);
            if (saveResult.IsFailure)
            {
                return Result.Fail(saveResult);
            }

            ResultValue = new DuplicateSheetResult(duplicate.Name, duplicate.Position);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to duplicate sheet '{SourceSheet}' in '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
