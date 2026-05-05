using Celbridge.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class SetActiveViewCommand : CommandBase, ISpreadsheetSetActiveViewCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public string Range { get; set; } = string.Empty;
    public string TopLeftCell { get; set; } = string.Empty;

    public SetActiveViewCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (!string.IsNullOrEmpty(Range)
            && Range.Contains('!'))
        {
            return Result.Fail("Range must not include a sheet qualifier; use the sheet parameter instead.");
        }

        if (!string.IsNullOrEmpty(TopLeftCell)
            && TopLeftCell.Contains('!'))
        {
            return Result.Fail("TopLeftCell must not include a sheet qualifier; use the sheet parameter instead.");
        }

        if (!string.IsNullOrEmpty(TopLeftCell)
            && TopLeftCell.Contains(':'))
        {
            return Result.Fail($"TopLeftCell must be a single cell address, was '{TopLeftCell}'.");
        }

        // Write view state to disk and let the file-watcher reload path apply it
        // to any open editor. The spreadsheet editor auto-saves selection and
        // active sheet on every change, so its in-memory state and disk stay in
        // sync; on reload, the imported workbook reflects whatever this command
        // just wrote, and the JS-side restoreViewState yields to disk when the
        // active sheet or selection has changed.
        return ApplyViewStateToWorkbook(workbookPath);
    }

    private Result ApplyViewStateToWorkbook(string workbookPath)
    {
        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            if (!workbook.Worksheets.Contains(Sheet))
            {
                return Result.Fail($"Sheet not found: '{Sheet}'.");
            }
            var worksheet = workbook.Worksheet(Sheet);

            // SetTabActive only updates the workbook-level active-tab pointer;
            // TabSelected is a per-sheet flag for the multi-tab selection group
            // (the Shift/Ctrl-click behaviour). Clear it on every sheet so the
            // result is a single selected tab rather than the target plus
            // whatever was previously selected.
            foreach (var otherSheet in workbook.Worksheets)
            {
                otherSheet.TabSelected = false;
            }
            worksheet.SetTabActive();
            worksheet.TabSelected = true;

            if (!string.IsNullOrEmpty(Range))
            {
                IXLRange selectionRange;
                try
                {
                    selectionRange = worksheet.Range(Range);
                }
                catch (Exception ex)
                {
                    return Result.Fail($"Invalid range: '{Range}'.").WithException(ex);
                }

                worksheet.ActiveCell = selectionRange.FirstCell();
                worksheet.SelectedRanges.RemoveAll(_ => true);
                worksheet.SelectedRanges.Add(selectionRange);
            }

            if (!string.IsNullOrEmpty(TopLeftCell))
            {
                IXLCell scrollAnchor;
                try
                {
                    scrollAnchor = worksheet.Cell(TopLeftCell);
                }
                catch (Exception ex)
                {
                    return Result.Fail($"Invalid top-left cell: '{TopLeftCell}'.").WithException(ex);
                }

                worksheet.SheetView.TopLeftCellAddress = scrollAnchor.Address;
            }

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to set active view on '{FileResource}'").WithException(ex);
        }
    }
}
