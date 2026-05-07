using Celbridge.Commands;
using Celbridge.Spreadsheet.Services;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class WriteCellsCommand : CommandBase, IWriteCellsCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public IReadOnlyList<CellEdit> Edits { get; set; } = Array.Empty<CellEdit>();

    public WriteCellsResult ResultValue { get; private set; } =
        new WriteCellsResult(0);

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
            return Result.Fail("At least one edit is required.");
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            if (!workbook.Worksheets.Contains(Sheet))
            {
                return Result.Fail($"Sheet not found: '{Sheet}'. Add it via spreadsheet_add_sheets first.");
            }
            var worksheet = workbook.Worksheet(Sheet);

            for (int editIndex = 0; editIndex < Edits.Count; editIndex++)
            {
                var edit = Edits[editIndex];

                if (string.IsNullOrEmpty(edit.Cell))
                {
                    return Result.Fail($"Edit {editIndex + 1}: cell address is required.");
                }

                IXLCell cell;
                try
                {
                    cell = worksheet.Cell(edit.Cell);
                }
                catch (Exception ex)
                {
                    return Result.Fail($"Edit {editIndex + 1}: invalid cell address '{edit.Cell}': {ex.Message}");
                }

                if (edit.IsFormula)
                {
                    var formulaText = edit.Value as string;
                    if (formulaText is null)
                    {
                        return Result.Fail($"Edit {editIndex + 1}: marked isFormula but value is not a string.");
                    }
                    SpreadsheetValueConverter.SetCellFormula(cell, formulaText);
                }
                else
                {
                    if (edit.Value is double doubleValue)
                    {
                        var validation = SpreadsheetCommandHelpers.ValidateNumericValue(doubleValue);
                        if (validation.IsFailure)
                        {
                            return Result.Fail($"Edit {editIndex + 1}: {validation.FirstErrorMessage}");
                        }
                    }
                    SpreadsheetValueConverter.SetCellValue(cell, edit.Value);
                }
            }

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            ResultValue = new WriteCellsResult(Edits.Count);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to write cells to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
