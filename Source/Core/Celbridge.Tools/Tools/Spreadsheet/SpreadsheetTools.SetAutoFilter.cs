using Celbridge.Spreadsheet;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by spreadsheet_set_auto_filter: enabled is true when the
/// sheet has an active auto-filter after the call, filterRange is the A1 range
/// the filter covers, or empty string when the filter was cleared.
/// </summary>
public record class SetAutoFilterResult(bool Enabled, string FilterRange);

public partial class SpreadsheetTools
{
    /// <summary>
    /// Adds or clears the auto-filter on a worksheet. Auto-filter shows the dropdown arrows in the
    /// header row that let the user filter and sort each column. Each sheet supports at most one
    /// auto-filter; setting a new one replaces any existing filter on the sheet. With enabled=false
    /// the existing filter (if any) is cleared and any rows hidden by the filter become visible again.
    /// Filtering UI is consumed in the spreadsheet editor — this tool only configures which range
    /// the filter applies to. Use spreadsheet_sort to reorder rows from the agent side.
    /// </summary>
    /// <param name="resource">Resource key of the .xlsx workbook.</param>
    /// <param name="sheet">Name of the worksheet to set the filter on.</param>
    /// <param name="range">A1 cell range to apply the filter to (e.g. "A1:F100"). Empty string applies the filter to the worksheet's entire used range. Ignored when enabled is false. Column-letter and row-number ranges are rejected.</param>
    /// <param name="enabled">True (default) applies an auto-filter to the given range. False clears any existing auto-filter on the sheet and ignores range.</param>
    /// <returns>JSON object with fields: enabled (bool, true if a filter is active after the call), filterRange (string, the A1 range the filter covers, or empty string when cleared).</returns>
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
            return ErrorResult(resolveResult.FirstErrorMessage);
        }

        if (string.IsNullOrEmpty(sheet))
        {
            return ErrorResult("Sheet name is required.");
        }

        var fileResourceKey = ResourceKey.Create(resource);
        var (callResult, commandResult) = await ExecuteCommandAsync<ISpreadsheetSetAutoFilterCommand, SpreadsheetSetAutoFilterResult>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Sheet = sheet;
            command.Range = range;
            command.Enabled = enabled;
        });
        if (callResult.IsError == true)
        {
            return callResult;
        }

        var commandValue = commandResult ?? new SpreadsheetSetAutoFilterResult(false, string.Empty);
        var result = new SetAutoFilterResult(commandValue.Enabled, commandValue.FilterRange);

        return SuccessResult(SerializeJson(result));
    }
}
