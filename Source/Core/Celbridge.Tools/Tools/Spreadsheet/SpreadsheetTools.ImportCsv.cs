using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Replaces the contents of one or more worksheets with RFC 4180 CSV data in a single save.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="importsJson">JSON array of imports with sheet, csvText, optional createIfMissing, and optional inferTypes. See guides_read(['spreadsheet_import_csv']) for parsing rules and inference behaviour.</param>
    /// <returns>JSON object with importsApplied, totalRowCount, and sheetsCreated.</returns>
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
