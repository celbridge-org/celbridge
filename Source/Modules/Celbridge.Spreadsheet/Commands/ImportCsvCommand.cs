using System.Globalization;
using Celbridge.Commands;
using Celbridge.Spreadsheet.Services;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class ImportCsvCommand : CommandBase, IImportCsvCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public IReadOnlyList<CsvImport> Imports { get; set; } = Array.Empty<CsvImport>();

    public ImportCsvResult ResultValue { get; private set; } =
        new ImportCsvResult(0, 0, 0);

    public ImportCsvCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        var resolveResult = SpreadsheetHelper.ResolveWorkbookPath(_workspaceWrapper, FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult.FirstErrorMessage);
        }
        var workbookPath = resolveResult.Value;

        if (Imports.Count == 0)
        {
            return Result.Fail("At least one CSV import is required.");
        }

        var parsedImports = new List<(CsvImport Import, IReadOnlyList<IReadOnlyList<string>> Rows)>(Imports.Count);
        var seenSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int importIndex = 0; importIndex < Imports.Count; importIndex++)
        {
            var import = Imports[importIndex];
            if (string.IsNullOrEmpty(import.Sheet))
            {
                return Result.Fail($"Import {importIndex + 1}: sheet name is required.");
            }
            if (!seenSheets.Add(import.Sheet))
            {
                return Result.Fail($"Import {importIndex + 1}: sheet '{import.Sheet}' appears more than once in the batch.");
            }

            IReadOnlyList<IReadOnlyList<string>> parsedRows;
            try
            {
                parsedRows = SpreadsheetCsvParser.Parse(import.CsvText);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Import {importIndex + 1} ('{import.Sheet}'): failed to parse CSV text: {ex.Message}");
            }

            if (parsedRows.Count > 1)
            {
                var expectedColumnCount = parsedRows[0].Count;
                for (int rowIndex = 1; rowIndex < parsedRows.Count; rowIndex++)
                {
                    if (parsedRows[rowIndex].Count != expectedColumnCount)
                    {
                        return Result.Fail(
                            $"Import {importIndex + 1} ('{import.Sheet}'): CSV row {rowIndex + 1} has {parsedRows[rowIndex].Count} fields, expected {expectedColumnCount} (matching row 1).");
                    }
                }
            }

            parsedImports.Add((import, parsedRows));
        }

        int totalRowCount = 0;
        int sheetsCreated = 0;

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            for (int importIndex = 0; importIndex < parsedImports.Count; importIndex++)
            {
                var (import, parsedRows) = parsedImports[importIndex];

                var sheetExists = workbook.Worksheets.Contains(import.Sheet);
                if (!sheetExists && !import.CreateIfMissing)
                {
                    return Result.Fail(
                        $"Import {importIndex + 1}: sheet not found: '{import.Sheet}'. Pass createIfMissing: true to create it, or call spreadsheet_add_sheets first.");
                }

                IXLWorksheet worksheet;
                if (sheetExists)
                {
                    worksheet = workbook.Worksheet(import.Sheet);
                    worksheet.Clear(XLClearOptions.Contents);
                }
                else
                {
                    worksheet = workbook.Worksheets.Add(import.Sheet);
                    sheetsCreated++;
                }

                for (int rowIndex = 0; rowIndex < parsedRows.Count; rowIndex++)
                {
                    var fields = parsedRows[rowIndex];
                    for (int columnIndex = 0; columnIndex < fields.Count; columnIndex++)
                    {
                        var cell = worksheet.Cell(rowIndex + 1, columnIndex + 1);
                        if (import.InferTypes)
                        {
                            ApplyInferredValue(cell, fields[columnIndex]);
                        }
                        else
                        {
                            cell.Value = fields[columnIndex];
                        }
                    }
                }

                totalRowCount += parsedRows.Count;
            }

            SpreadsheetHelper.RecalculateAndSave(workbook);

            ResultValue = new ImportCsvResult(parsedImports.Count, totalRowCount, sheetsCreated);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to import CSV into '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }

    // Conservative CSV type inference. Empty string becomes a blank cell;
    // case-insensitive "true"/"false" become booleans; integer-shaped fields
    // without a leading zero (so zip codes like "01234" stay text) become
    // integers; decimal-shaped fields (no scientific notation, so product
    // codes like "1e10" stay text) become doubles. Anything else stays as a
    // string so the round-trip is lossless for unfamiliar formats.
    private static void ApplyInferredValue(IXLCell cell, string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            cell.Value = string.Empty;
            return;
        }

        if (string.Equals(field, "true", StringComparison.OrdinalIgnoreCase))
        {
            cell.Value = true;
            return;
        }

        if (string.Equals(field, "false", StringComparison.OrdinalIgnoreCase))
        {
            cell.Value = false;
            return;
        }

        if (LooksLikeInteger(field) && long.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
        {
            cell.Value = integerValue;
            return;
        }

        if (LooksLikeDecimal(field) && double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue)
            && double.IsFinite(decimalValue))
        {
            cell.Value = decimalValue;
            return;
        }

        cell.Value = field;
    }

    // Plain integer: optional leading minus, then one or more digits, no
    // leading zero unless the value is exactly "0" or "-0". Preserves zip
    // codes, IDs, and similar zero-padded identifiers.
    private static bool LooksLikeInteger(string field)
    {
        var startIndex = field[0] == '-' ? 1 : 0;
        if (startIndex >= field.Length)
        {
            return false;
        }

        if (field[startIndex] == '0' && field.Length - startIndex > 1)
        {
            return false;
        }

        for (int characterIndex = startIndex; characterIndex < field.Length; characterIndex++)
        {
            if (field[characterIndex] < '0' || field[characterIndex] > '9')
            {
                return false;
            }
        }

        return true;
    }

    // Plain decimal: optional leading minus, digits, exactly one '.', more
    // digits. No exponent character, so "1e10" stays a string.
    private static bool LooksLikeDecimal(string field)
    {
        var startIndex = field[0] == '-' ? 1 : 0;
        var dotIndex = -1;
        for (int characterIndex = startIndex; characterIndex < field.Length; characterIndex++)
        {
            var character = field[characterIndex];
            if (character == '.')
            {
                if (dotIndex >= 0)
                {
                    return false;
                }
                dotIndex = characterIndex;
                continue;
            }
            if (character < '0' || character > '9')
            {
                return false;
            }
        }

        return dotIndex > startIndex && dotIndex < field.Length - 1;
    }
}
