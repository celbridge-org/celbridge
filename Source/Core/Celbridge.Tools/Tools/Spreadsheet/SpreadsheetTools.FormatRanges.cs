using System.Text.Json;
using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Applies a batch of format edits across one or more sheets in a single save.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="editsJson">JSON array of edits with sheet, range, and format fields. See guides_read(['spreadsheet_format_ranges']) for the format spec keys, units, and clear sentinels.</param>
    /// <returns>JSON object with editsApplied, propertiesApplied, and autoFitApplied.</returns>
    [McpServerTool(Name = "spreadsheet_format_ranges")]
    [ToolAlias("spreadsheet.format_ranges")]
    public async partial Task<CallToolResult> FormatRanges(string resource, string editsJson)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }

        var parseResult = ParseFormatEdits(editsJson);
        if (parseResult.IsFailure)
        {
            return ToolError(parseResult);
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
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
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
