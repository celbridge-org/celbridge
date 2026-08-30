using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// A single entry in a utilities state snapshot: one button on the Utility Panel rail, whatever it presents.
/// CurrentArea is where the entry is now, and is null when nothing presents it. IsVisible is true when the
/// user can see it: the selected rail item or the active document, in an area that is not collapsed. Resource is the file the entry presents, and is empty for a rail item that has none.
/// DockArea is where the entry opens as a document, and is null for a utility that stays in the panel.
/// </summary>
public record class UtilityInfo(
    EditorId UtilityId,
    string DisplayName,
    WorkspaceArea? CurrentArea,
    WorkspaceArea? DockArea,
    bool IsVisible,
    ResourceKey Resource);

/// <summary>
/// Snapshot of every button the Utility Panel rail carries.
/// </summary>
public record class UtilitiesStateSnapshot(
    IReadOnlyList<UtilityInfo> Utilities);

/// <summary>
/// Read-only query that snapshots what the Utility Panel rail offers and how each entry is currently
/// presented.
/// </summary>
public interface IGetUtilitiesStateCommand : IExecutableCommand<UtilitiesStateSnapshot>
{
}
