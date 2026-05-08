using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Apply font, fill, border, alignment, and number-format edits to cell ranges.</summary>
    [McpServerTool(Name = "spreadsheet_format_ranges")]
    [ToolAlias("spreadsheet.format_ranges")]
    public async partial Task<CallToolResult> FormatRanges(string resource, string editsJson)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }

        var parseResult = ParseFormatEdits(editsJson);
        if (parseResult.IsFailure)
        {
            return ToolResponse.Error(parseResult);
        }
        var edits = parseResult.Value;

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<IFormatRangesCommand, FormatRangesResult>(command =>
        {
            command.FileResource = fileResourceKey;
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

    private static Result<IReadOnlyList<FormatEdit>> ParseFormatEdits(string editsJson)
    {
        if (string.IsNullOrEmpty(editsJson))
        {
            return Result.Fail("Edits JSON is required.");
        }

        try
        {
            var edits = JsonSerializer.Deserialize<List<FormatEdit>>(editsJson, JsonOptions);
            if (edits is null)
            {
                return Result.Fail("Edits JSON must be a non-null array.");
            }
            if (edits.Count == 0)
            {
                return Result.Fail("Edits array must contain at least one edit.");
            }
            return edits;
        }
        catch (JsonException ex)
        {
            return Result.Fail($"Invalid edits JSON: {ex.Message}");
        }
    }
}
