using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Apply font, fill, border, alignment, and number-format edits to cell ranges.</summary>
    [McpServerTool(Name = "spreadsheet_format_ranges")]
    [ToolAlias("spreadsheet.format_ranges")]
    [RelatedGuides("resource_keys", "spreadsheet_a1_notation", "spreadsheet_editor_division")]
    public async partial Task<CallToolResult> FormatRanges(string resource, string editsJson)
    {
        var resolveResult = await ResolveWorkbookResourceAsync(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        var parseResult = ParseJsonArgument<List<FormatEdit>>(editsJson, "edits JSON");
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var edits = parseResult.Value;
        if (edits.Count == 0)
        {
            return ToolResponse.Error("Edits array must contain at least one edit.");
        }

        var commandResult = await ExecuteCommandAsync<IFormatRangesCommand, FormatRangesResult>(command =>
        {
            command.FileResource = workbookResource;
            command.Edits = edits;
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
