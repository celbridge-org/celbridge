using Celbridge.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class MoveSheetCommand : CommandBase, IMoveSheetCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public int Position { get; set; }

    public MoveSheetCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (Position < 1)
        {
            return Result.Fail($"Position must be 1 or greater, was {Position}.");
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

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
            if (worksheet.Position == Position)
            {
                return Result.Ok();
            }

            worksheet.Position = Position;
            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to move sheet '{Sheet}' in '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
