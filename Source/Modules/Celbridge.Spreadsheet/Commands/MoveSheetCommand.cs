using Celbridge.Commands;
using Celbridge.Spreadsheet.Helpers;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class MoveSheetCommand : CommandBase, IMoveSheetCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public int Position { get; set; }

    public MoveSheetResult ResultValue { get; private set; } =
        new MoveSheetResult(string.Empty, 0);

    public MoveSheetCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (Position < 1)
        {
            return Result.Fail($"Position must be 1 or greater, was {Position}.");
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

            var sheetCount = workbook.Worksheets.Count;
            if (Position > sheetCount)
            {
                return Result.Fail($"Position {Position} is out of range; workbook has {sheetCount} sheet(s).");
            }

            var worksheet = workbook.Worksheet(Sheet);
            if (worksheet.Position != Position)
            {
                worksheet.Position = Position;
                var saveResult = await SpreadsheetHelper.SaveWorkbookAsync(fileSystem, workbookResource, workbook);
                if (saveResult.IsFailure)
                {
                    return Result.Fail(saveResult);
                }
            }

            ResultValue = new MoveSheetResult(Sheet, Position);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to move sheet '{Sheet}' in '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
