using Celbridge.Commands;
using Celbridge.Spreadsheet.Helpers;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class SetAutoFilterCommand : CommandBase, ISetAutoFilterCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public string Range { get; set; } = string.Empty;
    public bool Enabled { get; set; }

    public SetAutoFilterResult ResultValue { get; private set; } =
        new SetAutoFilterResult(false, string.Empty);

    public SetAutoFilterCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (Enabled
            && !string.IsNullOrEmpty(Range)
            && (SpreadsheetHelper.IsColumnRange(Range) || SpreadsheetHelper.IsRowRange(Range)))
        {
            return Result.Fail($"Auto-filter range must be an A1 cell range like 'A1:F100', was '{Range}'.");
        }

        var fileStorage = _workspaceWrapper.WorkspaceService.FileStorage;
        var loadResult = await SpreadsheetHelper.LoadWorkbookAsync(fileStorage, workbookResource);
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
            var worksheet = workbook.Worksheet(Sheet);

            if (!Enabled)
            {
                if (worksheet.AutoFilter.IsEnabled)
                {
                    worksheet.AutoFilter.Clear();
                }
                ResultValue = new SetAutoFilterResult(false, string.Empty);
            }
            else
            {
                IXLRange filterRange;
                if (string.IsNullOrEmpty(Range))
                {
                    var usedRange = worksheet.RangeUsed();
                    if (usedRange is null)
                    {
                        return Result.Fail($"Cannot apply auto-filter to '{Sheet}': sheet is empty.");
                    }
                    filterRange = usedRange;
                }
                else
                {
                    try
                    {
                        filterRange = worksheet.Range(Range);
                    }
                    catch (Exception ex)
                    {
                        return Result.Fail($"Invalid cell range '{Range}': {ex.Message}");
                    }
                }

                filterRange.SetAutoFilter();
                ResultValue = new SetAutoFilterResult(true, filterRange.RangeAddress.ToStringRelative());
            }

            var saveResult = await SpreadsheetHelper.SaveWorkbookAsync(fileStorage, workbookResource, workbook);
            if (saveResult.IsFailure)
            {
                return Result.Fail(saveResult);
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to set auto-filter on '{Sheet}' in '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
