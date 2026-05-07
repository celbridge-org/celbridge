using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Replaces the contents of one or more worksheets with parsed CSV data in a single open/save cycle.
    /// The CSV is parsed per RFC 4180 (comma delimiter, double-quote quoting, embedded quotes doubled,
    /// CRLF or LF line endings). All rows in each CSV must have the same field count as that CSV's
    /// row 1. Existing cells in each target sheet are cleared before the CSV block is written. Other
    /// sheets in the workbook are untouched. Imports run in order. If any import fails the whole batch
    /// fails and nothing is saved. By default, plain integer / decimal / boolean fields are written
    /// as typed cell values so SUM, sorting and conditional-formatting numeric rules work; pass
    /// inferTypes:false on a per-import basis to keep every field as a string. Inference is
    /// deliberately conservative: integer-shaped fields with a leading zero (zip codes, IDs) stay
    /// as strings, fields containing scientific notation (product codes like "1e10") stay as
    /// strings, and dates are not inferred. CSV imports do not produce formula cells, but formulas
    /// elsewhere in the workbook are recalculated as part of the save.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="importsJson">JSON array of imports. Each import is an object with sheet (string), csvText (string), optional createIfMissing (bool, default false), and optional inferTypes (bool, default true). createIfMissing creates a missing sheet rather than failing the batch. inferTypes true converts plain integer / decimal / boolean fields into typed cell values; false keeps every field as a string.</param>
    /// <returns>JSON object with fields: importsApplied (int), totalRowCount (int, summed across imports), sheetsCreated (int, count of imports that added a new sheet).</returns>
    [McpServerTool(Name = "spreadsheet_import_csv")]
    [ToolAlias("spreadsheet.import_csv")]
    public async partial Task<CallToolResult> ImportCsv(string resource, string importsJson)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }

        var parseResult = ParseCsvImports(importsJson);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
        }
        var imports = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IImportCsvCommand, ImportCsvResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Imports = imports;
        });
        if (commandResult.IsFailure)
        {
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
    }

    private static Result<IReadOnlyList<CsvImport>> ParseCsvImports(string importsJson)
    {
        if (string.IsNullOrEmpty(importsJson))
        {
            return Result.Fail("Imports JSON is required.");
        }

        try
        {
            var imports = JsonSerializer.Deserialize<List<CsvImport>>(importsJson, JsonOptions);
            if (imports is null)
            {
                return Result.Fail("Imports JSON must be a non-null array.");
            }
            if (imports.Count == 0)
            {
                return Result.Fail("Imports array must contain at least one import.");
            }
            return imports;
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Invalid imports JSON: {ex.Message}");
        }
    }
}
