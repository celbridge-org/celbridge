using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class SpreadsheetTools
{
    /// <summary>
    /// Returns guidance for working with .xlsx workbooks via the spreadsheet_* tools:
    /// A1 notation, JSON cell typing, the recalculate-after-formula-write rule,
    /// headers mode, paging, the editor-vs-tools division of labour, and common workflows.
    /// </summary>
    /// <returns>A Markdown document describing how to use the spreadsheet_* MCP tools.</returns>
    [McpServerTool(Name = "spreadsheet_get_context", ReadOnly = true, Idempotent = true)]
    [ToolAlias("spreadsheet.get_context")]
    public partial CallToolResult GetContext()
    {
        return SuccessResult(LoadEmbeddedResource("Celbridge.Tools.Assets.SpreadsheetContext.md"));
    }
}
