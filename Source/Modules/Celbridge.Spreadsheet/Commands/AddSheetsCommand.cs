using Celbridge.Commands;
using Celbridge.Spreadsheet.Helpers;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class AddSheetsCommand : CommandBase, IAddSheetsCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public IReadOnlyList<string> Sheets { get; set; } = Array.Empty<string>();

    public AddSheetsResult ResultValue { get; private set; } =
        new AddSheetsResult(Array.Empty<string>());

    public AddSheetsCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (Sheets.Count == 0)
        {
            return Result.Fail("At least one sheet name is required.");
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int sheetIndex = 0; sheetIndex < Sheets.Count; sheetIndex++)
        {
            var sheetName = Sheets[sheetIndex];
            if (string.IsNullOrEmpty(sheetName))
            {
                return Result.Fail($"Sheet name at index {sheetIndex} is empty.");
            }
            if (!seenNames.Add(sheetName))
            {
                return Result.Fail($"Duplicate sheet name in batch: '{sheetName}'.");
            }
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

            foreach (var sheetName in Sheets)
            {
                if (workbook.Worksheets.Contains(sheetName))
                {
                    return Result.Fail($"Sheet already exists: '{sheetName}'.");
                }
            }

            foreach (var sheetName in Sheets)
            {
                workbook.Worksheets.Add(sheetName);
            }

            var saveResult = await SpreadsheetHelper.SaveWorkbookAsync(fileStorage, workbookResource, workbook);
            if (saveResult.IsFailure)
            {
                return Result.Fail(saveResult);
            }

            ResultValue = new AddSheetsResult(Sheets.ToList());
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to add sheets to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
