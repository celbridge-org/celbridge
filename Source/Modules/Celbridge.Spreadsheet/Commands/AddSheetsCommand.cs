using Celbridge.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class AddSheetsCommand : CommandBase, ISpreadsheetAddSheetsCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public IReadOnlyList<string> Sheets { get; set; } = Array.Empty<string>();

    public SpreadsheetAddSheetsResult ResultValue { get; private set; } =
        new SpreadsheetAddSheetsResult(Array.Empty<string>());

    public AddSheetsCommand(IWorkspaceWrapper workspaceWrapper)
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

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

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

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            ResultValue = new SpreadsheetAddSheetsResult(Sheets.ToList());
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to add sheets to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
