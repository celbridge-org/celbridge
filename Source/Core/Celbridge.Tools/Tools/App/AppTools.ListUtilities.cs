using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A single utility in the app_list_utilities result. CurrentArea is "utility" (shown in the Utility Panel),
/// a document area token (presented as a document tab in that area), or empty when nothing presents it.
/// DockArea is the area token this entry opens as a document in, and is empty for a utility that stays in the
/// panel. IsVisible reports whether the user can see it, which needs both that it is selected and that its
/// area is not collapsed. Resource is the file the utility presents, empty when it has none.
/// </summary>
public record class UtilityListEntry(
    string UtilityId,
    string DisplayName,
    string CurrentArea,
    string DockArea,
    bool IsVisible,
    string Resource);

/// <summary>
/// Result returned by app_list_utilities: every utility the Utility Panel rail offers.
/// </summary>
public record class UtilitiesListResult(
    IReadOnlyList<UtilityListEntry> Utilities);

public partial class AppTools
{
    /// <summary>List every utility on the Utility Panel rail with its area and shown state.</summary>
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
            // An entry nothing presents reports no area, which is a launcher whose document is closed.
            var currentArea = utility.CurrentArea is null
                ? string.Empty
                : utility.CurrentArea.Value.ToToken();

            // A utility that stays in the panel reports no document area rather than a token that would
            // read as somewhere it can be sent.
            var dockArea = utility.DockArea is null
                ? string.Empty
                : utility.DockArea.Value.ToToken();

            // A utility with no file behind it reports an empty resource rather than the empty key's own
            // "project:" spelling, which would read as a resource that exists.
            var resource = utility.Resource.IsEmpty ? string.Empty : utility.Resource.ToString();

            entries.Add(new UtilityListEntry(
                utility.UtilityId.ToString(),
                utility.DisplayName,
                currentArea,
                dockArea,
                utility.IsVisible,
                resource));
        }

        var result = new UtilitiesListResult(entries);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
