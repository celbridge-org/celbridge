using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Adds or clears the auto-filter on a worksheet. Each sheet supports at most one auto-filter.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Worksheet to set the filter on.</param>
    /// <param name="range">A1 cell range. Empty applies to the used range. Column-letter and row-number ranges are rejected. Ignored when enabled is false.</param>
    /// <param name="enabled">True applies the filter; false clears any existing filter.</param>
    /// <returns>JSON object with enabled and filterRange.</returns>
    [McpServerTool(Name = "spreadsheet_set_auto_filter")]
    [ToolAlias("spreadsheet.set_auto_filter")]
    public async partial Task<CallToolResult> SetAutoFilter(
        string resource,
        string sheet,
        string range = "",
        bool enabled = true)
    {
        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolError(resolveResult);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolError("Sheet name is required.");
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var commandResult = await ExecuteCommandAsync<ISetAutoFilterCommand, SetAutoFilterResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Range = range;
            command.Enabled = enabled;
        });
        if (commandResult.IsFailure)
        {
            return ToolError(commandResult);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolSuccess(json);
    }
}
