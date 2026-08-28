using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A single utility in the app_list_utilities result. Area is "utility" (a Utility Panel rail surface) or a
/// document area token (docked as a document tab in that area). IsShown reports whether the utility is
/// currently surfaced to the user: the active rail surface when in the panel, or the active document
/// when docked as a document.
/// </summary>
public record class UtilityListEntry(
    string UtilityId,
    string DisplayName,
    string Area,
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
            var area = utility.Area.ToToken();
            entries.Add(new UtilityListEntry(
                utility.UtilityId.ToString(),
                utility.DisplayName,
                area,
                utility.IsShown));
        }

        var result = new UtilitiesListResult(entries);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
