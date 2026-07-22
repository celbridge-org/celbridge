using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// A single utility in a utilities state snapshot: a built-in Utility Panel surface (Explorer, Search) or a
/// package-contributed utility. IsShown is true when the utility is currently surfaced to the user, either as
/// the active rail surface or as the active document.
/// </summary>
public record class UtilityInfo(
    EditorId UtilityId,
    string DisplayName,
    DockLocation Location,
    bool IsShown);

/// <summary>
/// Snapshot of every available utility, built-in and custom.
/// </summary>
public record class UtilitiesStateSnapshot(
    IReadOnlyList<UtilityInfo> Utilities);

/// <summary>
/// Read-only query that snapshots the catalog of available utilities (built-in and custom) and their
/// current shown state.
/// </summary>
public interface IGetUtilitiesStateCommand : IExecutableCommand<UtilitiesStateSnapshot>
{
}
