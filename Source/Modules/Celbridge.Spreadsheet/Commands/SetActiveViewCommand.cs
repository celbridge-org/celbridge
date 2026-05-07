using Celbridge.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class SetActiveViewCommand : CommandBase, ISetActiveViewCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public string Range { get; set; } = string.Empty;
    public IReadOnlyList<string> Ranges { get; set; } = Array.Empty<string>();
    public string ActiveCell { get; set; } = string.Empty;
    public string TopLeftCell { get; set; } = string.Empty;

    public SetActiveViewResult ResultValue { get; private set; } =
        new SetActiveViewResult(string.Empty, string.Empty, Array.Empty<string>(), string.Empty, string.Empty);

    public SetActiveViewCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (!string.IsNullOrEmpty(Range)
            && Range.Contains('!'))
        {
            return Result.Fail("Range must not include a sheet qualifier; use the sheet parameter instead.");
        }

        for (int rangeIndex = 0; rangeIndex < Ranges.Count; rangeIndex++)
        {
            var rangeEntry = Ranges[rangeIndex];
            if (string.IsNullOrEmpty(rangeEntry))
            {
                return Result.Fail($"Ranges[{rangeIndex}] is empty.");
            }
            if (rangeEntry.Contains('!'))
            {
                return Result.Fail($"Ranges[{rangeIndex}] must not include a sheet qualifier; use the sheet parameter instead.");
            }
        }

        if (!string.IsNullOrEmpty(ActiveCell)
            && ActiveCell.Contains('!'))
        {
            return Result.Fail("ActiveCell must not include a sheet qualifier; use the sheet parameter instead.");
        }

        if (!string.IsNullOrEmpty(ActiveCell)
            && ActiveCell.Contains(':'))
        {
            return Result.Fail($"ActiveCell must be a single cell address, was '{ActiveCell}'.");
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

        // Write to disk and let the file-watcher reload path apply the view
        // state to any open editor. The editor's restoreViewState yields to
        // disk when the active sheet or selection has changed.
        var applyResult = ApplyViewStateToWorkbook(workbookPath);
        if (applyResult.IsFailure)
        {
            return Result.Fail(applyResult.FirstErrorMessage);
        }
        var applied = applyResult.Value;

        var appliedRange = applied.Ranges.Count > 0 ? applied.Ranges[0] : string.Empty;
        ResultValue = new SetActiveViewResult(Sheet, appliedRange, applied.Ranges, applied.ActiveCell, TopLeftCell);
        return Result.Ok();
    }

    // Selection state actually applied to the workbook. Ranges echoes the
    // input (either Ranges, or [Range], or empty). ActiveCell is the resolved
    // anchor address: explicit when ActiveCell was supplied, otherwise the
    // first cell of the first selection range.
    private record AppliedViewState(IReadOnlyList<string> Ranges, string ActiveCell);

    private Result<AppliedViewState> ApplyViewStateToWorkbook(string workbookPath)
    {
        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            if (!workbook.Worksheets.Contains(Sheet))
            {
                return Result.Fail($"Sheet not found: '{Sheet}'.");
            }
            var worksheet = workbook.Worksheet(Sheet);

            // SetTabActive sets the workbook-level pointer only; TabSelected is
            // the per-sheet multi-tab selection flag. Clear it everywhere so
            // the target ends up as the sole selected tab.
            foreach (var otherSheet in workbook.Worksheets)
            {
                otherSheet.TabSelected = false;
            }
            worksheet.SetTabActive();
            worksheet.TabSelected = true;

            var hasRanges = Ranges.Count > 0;
            var hasRange = !string.IsNullOrEmpty(Range);
            var hasActiveCell = !string.IsNullOrEmpty(ActiveCell);

            IReadOnlyList<string> appliedRanges = Array.Empty<string>();
            var appliedActiveCell = string.Empty;

            if (hasRanges
                || hasRange
                || hasActiveCell)
            {
                var selectionResult = ResolveSelectionRanges(worksheet, hasRanges, hasRange, hasActiveCell);
                if (selectionResult.IsFailure)
                {
                    return Result.Fail(selectionResult.FirstErrorMessage);
                }
                var selectionRanges = selectionResult.Value;

                IXLCell anchorCell;
                if (hasActiveCell)
                {
                    try
                    {
                        anchorCell = worksheet.Cell(ActiveCell);
                    }
                    catch (Exception ex)
                    {
                        return Result.Fail($"Invalid ActiveCell: '{ActiveCell}'.").WithException(ex);
                    }

                    if ((hasRanges || hasRange)
                        && !AddressIsInsideAnyRange(selectionRanges, anchorCell.Address))
                    {
                        var rangeLabel = hasRanges ? string.Join(", ", Ranges) : Range;
                        return Result.Fail($"ActiveCell '{ActiveCell}' must lie inside one of the selection ranges ({rangeLabel}).");
                    }
                }
                else
                {
                    anchorCell = selectionRanges[0].FirstCell();
                }

                worksheet.ActiveCell = anchorCell;
                worksheet.SelectedRanges.RemoveAll();
                foreach (var selectionRange in selectionRanges)
                {
                    worksheet.SelectedRanges.Add(selectionRange);
                }

                // Echo the input addresses back. ActiveCell-only inputs become
                // a single-cell selection equal to that cell.
                if (hasRanges)
                {
                    appliedRanges = Ranges;
                }
                else if (hasRange)
                {
                    appliedRanges = new[] { Range };
                }
                else
                {
                    appliedRanges = new[] { ActiveCell };
                }

                appliedActiveCell = anchorCell.Address.ToStringRelative();
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

            SpreadsheetHelper.RecalculateAndSave(workbook);

            return new AppliedViewState(appliedRanges, appliedActiveCell);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to set active view on '{FileResource}'").WithException(ex);
        }
    }

    private Result<List<IXLRange>> ResolveSelectionRanges(IXLWorksheet worksheet, bool hasRanges, bool hasRange, bool hasActiveCell)
    {
        var resolved = new List<IXLRange>();

        if (hasRanges)
        {
            for (int rangeIndex = 0; rangeIndex < Ranges.Count; rangeIndex++)
            {
                var rangeEntry = Ranges[rangeIndex];
                IXLRange xlRange;
                try
                {
                    xlRange = worksheet.Range(rangeEntry);
                }
                catch (Exception ex)
                {
                    return Result.Fail($"Invalid range Ranges[{rangeIndex}]: '{rangeEntry}'.").WithException(ex);
                }
                resolved.Add(xlRange);
            }
            return resolved;
        }

        // Single-range path. ActiveCell-only inputs collapse to a single-cell
        // selection; range-only inputs default the active cell to the range's
        // first cell.
        var selectionRangeAddress = hasRange ? Range : ActiveCell;
        IXLRange selectionRange;
        try
        {
            selectionRange = worksheet.Range(selectionRangeAddress);
        }
        catch (Exception ex)
        {
            var label = hasRange ? "range" : "ActiveCell";
            return Result.Fail($"Invalid {label}: '{selectionRangeAddress}'.").WithException(ex);
        }

        resolved.Add(selectionRange);
        return resolved;
    }

    private static bool AddressIsInsideAnyRange(List<IXLRange> ranges, IXLAddress cellAddress)
    {
        foreach (var range in ranges)
        {
            if (RangeContainsAddress(range.RangeAddress, cellAddress))
            {
                return true;
            }
        }
        return false;
    }

    private static bool RangeContainsAddress(IXLRangeAddress rangeAddress, IXLAddress cellAddress)
    {
        return cellAddress.RowNumber >= rangeAddress.FirstAddress.RowNumber
            && cellAddress.RowNumber <= rangeAddress.LastAddress.RowNumber
            && cellAddress.ColumnNumber >= rangeAddress.FirstAddress.ColumnNumber
            && cellAddress.ColumnNumber <= rangeAddress.LastAddress.ColumnNumber;
    }
}
