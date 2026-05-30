using Celbridge.Commands;
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
        var resolveResult = await SpreadsheetHelper.ResolveWorkbookResourceAsync(_workspaceWrapper, FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        if (string.IsNullOrEmpty(Sheet))
        {
            return Result.Fail("Sheet name is required.");
        }

        if (Edits.Count == 0)
        {
            return Result.Fail("At least one edit is required.");
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
                    ValueConverterHelper.SetCellFormula(cell, formulaText);
                }
                else
                {
                    if (edit.Value is double doubleValue)
                    {
                        var validationResult = SpreadsheetHelper.ValidateNumericValue(doubleValue);
                        if (validationResult.IsFailure)
                        {
                            return Result.Fail($"Edit {editIndex + 1}: {validationResult.FirstErrorMessage}");
                        }
                    }
                    ValueConverterHelper.SetCellValue(cell, edit.Value);
                }
            }

            var saveResult = await SpreadsheetHelper.SaveWorkbookAsync(fileStorage, workbookResource, workbook);
            if (saveResult.IsFailure)
            {
                return Result.Fail(saveResult);
            }

            ResultValue = new WriteCellsResult(Edits.Count);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to write cells to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
