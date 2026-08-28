using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A single utility in the app_list_utilities result. Area is "utility" (a Utility Panel rail surface) or a
/// document area token (presented as a document tab in that area), and AllowedAreas are the area tokens it
/// may be moved to. IsShown reports whether the utility is currently surfaced to the user: the active rail
/// surface when in the panel, or the active document when it is a document. Resource is the file the utility
/// presents, empty when it has none.
/// </summary>
public record class UtilityListEntry(
    string UtilityId,
    string DisplayName,
    string Area,
    IReadOnlyList<string> AllowedAreas,
    bool IsShown,
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
            var area = utility.Area.ToToken();

            var allowedAreas = new List<string>(utility.AllowedAreas.Count);
            foreach (var allowedArea in utility.AllowedAreas)
            {
                allowedAreas.Add(allowedArea.ToToken());
            }

            // A utility with no file behind it reports an empty resource rather than the empty key's own
            // "project:" spelling, which would read as a resource that exists.
            var resource = utility.Resource.IsEmpty ? string.Empty : utility.Resource.ToString();

            entries.Add(new UtilityListEntry(
                utility.UtilityId.ToString(),
                utility.DisplayName,
                area,
                allowedAreas,
                utility.IsShown,
                resource));
        }

        var result = new UtilitiesListResult(entries);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
