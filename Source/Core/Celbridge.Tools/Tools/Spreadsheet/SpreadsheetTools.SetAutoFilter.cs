using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>Apply or clear the auto-filter on a worksheet over a header range.</summary>
    [McpServerTool(Name = "spreadsheet_set_auto_filter")]
    [ToolAlias("spreadsheet.set_auto_filter")]
    public async partial Task<CallToolResult> SetAutoFilter(
        string resource,
        string sheet,
        string range = "",
        bool enabled = true)
    {
        const string ToolGuide = "spreadsheet_set_auto_filter";

        var resolveResult = ResolveWorkbookPath(resource);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult, ToolGuide);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ToolResponse.Error("Sheet name is required.", ToolGuide);
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
            return ToolResponse.Error(commandResult, ToolGuide);
        }

        var commandValue = commandResult.Value;
        var json = SerializeJson(commandValue);
        return ToolResponse.Success(json);
    }
}
