using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A single utility in the app_list_utilities result. Location is "panel" (a Utility Panel rail surface) or
/// "document" (docked as a document tab). IsShown reports whether the utility is currently surfaced to the
/// user: the active rail surface when in the panel, or the active document when docked as a document.
/// </summary>
public record class UtilityListEntry(
    string UtilityId,
    string DisplayName,
    string Location,
    bool IsShown);

/// <summary>
/// Result returned by app_list_utilities: the catalog of every available utility, built-in and custom.
/// </summary>
public record class UtilitiesListResult(
    IReadOnlyList<UtilityListEntry> Utilities);

public partial class AppTools
{
    /// <summary>List every available utility (built-in and custom) with its shown state.</summary>
    [McpServerTool(Name = "app_list_utilities", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.list_utilities")]
    [RelatedGuides("workspace_panels")]
    public async partial Task<CallToolResult> ListUtilities()
    {
        var stateResult = await ExecuteCommandAsync<IGetUtilitiesStateCommand, UtilitiesStateSnapshot>();
        if (stateResult.IsFailure)
        {
            return ToolResponse.Error(stateResult);
        }
        var snapshot = stateResult.Value;

        var entries = new List<UtilityListEntry>(snapshot.Utilities.Count);
        foreach (var utility in snapshot.Utilities)
        {
            var location = DockLocationTokens.ToToken(utility.Location);
            entries.Add(new UtilityListEntry(
                utility.UtilityId.ToString(),
                utility.DisplayName,
                location,
                utility.IsShown));
        }

        var result = new UtilitiesListResult(entries);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
