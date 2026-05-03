using Celbridge.Commands;
using Celbridge.Spreadsheet.Tools;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class ImportCsvCommand : CommandBase, ISpreadsheetImportCsvCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public string CsvText { get; set; } = string.Empty;
    public bool CreateIfMissing { get; set; }

    public SpreadsheetImportCsvResult ResultValue { get; private set; } = new SpreadsheetImportCsvResult(0, 0, false);

    public ImportCsvCommand(IWorkspaceWrapper workspaceWrapper)
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

        IReadOnlyList<IReadOnlyList<string>> parsedRows;
        try
        {
            parsedRows = SpreadsheetCsvParser.Parse(CsvText);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to parse CSV text: {ex.Message}");
        }

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            var sheetExists = workbook.Worksheets.Contains(Sheet);
            if (!sheetExists && !CreateIfMissing)
            {
                return Result.Fail(
                    $"Sheet not found: '{Sheet}'. Pass createIfMissing: true to create it, or call spreadsheet_add_sheet first.");
            }

            IXLWorksheet worksheet;
            if (sheetExists)
            {
                worksheet = workbook.Worksheet(Sheet);
                worksheet.Clear(XLClearOptions.Contents);
            }
            else
            {
                worksheet = workbook.Worksheets.Add(Sheet);
            }

            int columnCount = 0;
            for (int rowIndex = 0; rowIndex < parsedRows.Count; rowIndex++)
            {
                var fields = parsedRows[rowIndex];
                if (fields.Count > columnCount)
                {
                    columnCount = fields.Count;
                }
                for (int columnIndex = 0; columnIndex < fields.Count; columnIndex++)
                {
                    var cell = worksheet.Cell(rowIndex + 1, columnIndex + 1);
                    cell.Value = fields[columnIndex];
                }
            }

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            ResultValue = new SpreadsheetImportCsvResult(parsedRows.Count, columnCount, !sheetExists);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to import CSV into '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }
}
