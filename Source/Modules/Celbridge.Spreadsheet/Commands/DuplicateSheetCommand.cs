using Celbridge.Commands;
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
        await Task.CompletedTask;

        var resolveResult = SpreadsheetCommandHelpers.ResolveWorkbookPath(_workspaceWrapper, FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult.FirstErrorMessage);
        }
        var workbookPath = resolveResult.Value;

        if (string.IsNullOrEmpty(SourceSheet))
        {
            return Result.Fail("Source sheet name is required.");
        }

        if (string.IsNullOrEmpty(NewSheet))
        {
            return Result.Fail("New sheet name is required.");
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

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

            IXLWorksheet duplicate;
            if (Position == 0)
            {
                duplicate = sourceWorksheet.CopyTo(NewSheet);
            }
            else
            {
                duplicate = sourceWorksheet.CopyTo(NewSheet, Position);
            }

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            ResultValue = new DuplicateSheetResult(duplicate.Name, duplicate.Position);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to duplicate sheet '{SourceSheet}' in '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
