using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Replace one or more worksheets with parsed CSV data, optionally creating missing sheets.</summary>
    [McpServerTool(Name = "spreadsheet_import_csv")]
    [ToolAlias("spreadsheet.import_csv")]
    [RelatedGuides("resource_keys", "spreadsheet_cell_typing", "spreadsheet_editor_division", "spreadsheet_workflows")]
    public async partial Task<CallToolResult> ImportCsv(string resource, string importsJson)
    {
        var resolveResult = await ResolveWorkbookResourceAsync(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        var parseResult = ParseJsonArgument<List<CsvImport>>(importsJson, "imports JSON");
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var imports = parseResult.Value;
        if (imports.Count == 0)
        {
            return ToolResponse.Error("Imports array must contain at least one import.");
        }

        var commandResult = await ExecuteCommandAsync<IImportCsvCommand, ImportCsvResult>(command =>
        {
            command.FileResource = workbookResource;
            command.Imports = imports;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolResponse.Success(json);
    }

}
