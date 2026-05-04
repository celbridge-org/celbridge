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

        // The spreadsheet editor reads view state from the .xlsx only on a fresh open.
        // While the document is open it caches view state in memory and ignores file-level
        // changes from external reloads, so writing new view state to disk has no visible
        // effect. We close the document tab before applying the change and reopen it
        // afterwards so the editor reads the new view state from disk.
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;
        var documentsPanel = _workspaceWrapper.WorkspaceService.DocumentsPanel;

        var openDocument = documentsService.GetOpenDocuments()
            .FirstOrDefault(d => d.FileResource == FileResource);
        var wasActive = openDocument is not null
            && documentsService.ActiveDocument == FileResource;

        if (openDocument is not null)
        {
            var closeResult = await documentsPanel.CloseDocument(FileResource, forceClose: true);
            if (closeResult.IsFailure)
            {
                return Result.Fail($"Failed to close document for view-state update: {closeResult.FirstErrorMessage}");
            }
        }

        var saveResult = ApplyViewStateToWorkbook(workbookPath);

        if (openDocument is not null)
        {
            // Reopen even on save failure so the user does not lose the document tab.
            var reopenOptions = new OpenDocumentOptions(
                Address: openDocument.Address,
                Activate: wasActive,
                EditorId: openDocument.EditorId);

            var reopenResult = await documentsPanel.OpenDocument(FileResource, reopenOptions);
            if (reopenResult.IsFailure
                && saveResult.IsSuccess)
            {
                return Result.Fail($"View state was saved but the document failed to reopen: {reopenResult.FirstErrorMessage}");
            }
        }

        return saveResult;
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

            worksheet.SetTabActive();

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
