using Celbridge.Commands;
using Celbridge.Spreadsheet.Tools;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class WriteCellsCommand : CommandBase, ISpreadsheetWriteCellsCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public IReadOnlyList<SpreadsheetCellEdit> Edits { get; set; } = Array.Empty<SpreadsheetCellEdit>();

    public WriteCellsCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (Edits.Count == 0)
        {
            return Result.Ok();
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            if (!workbook.Worksheets.Contains(Sheet))
            {
                return Result.Fail($"Sheet not found: '{Sheet}'. Add it via spreadsheet_add_sheet first.");
            }
            var worksheet = workbook.Worksheet(Sheet);

            for (int editIndex = 0; editIndex < Edits.Count; editIndex++)
            {
                var edit = Edits[editIndex];

                if (string.IsNullOrEmpty(edit.Cell))
                {
                    return Result.Fail($"Edit at index {editIndex} has an empty cell address.");
                }

                IXLCell cell;
                try
                {
                    cell = worksheet.Cell(edit.Cell);
                }
                catch (Exception ex)
                {
                    return Result.Fail($"Invalid cell address '{edit.Cell}' at edit index {editIndex}: {ex.Message}");
                }

                if (edit.IsFormula)
                {
                    var formulaText = edit.Value as string;
                    if (formulaText is null)
                    {
                        return Result.Fail($"Edit at index {editIndex} is marked isFormula but value is not a string.");
                    }
                    SpreadsheetValueConverter.SetCellFormula(cell, formulaText);
                }
                else
                {
                    SpreadsheetValueConverter.SetCellValue(cell, edit.Value);
                }
            }

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to write cells to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
