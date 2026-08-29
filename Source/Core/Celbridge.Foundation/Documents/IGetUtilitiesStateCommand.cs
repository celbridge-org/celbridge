using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// A single entry in a utilities state snapshot: one button on the Utility Panel rail, whatever it presents.
/// IsShown is true when it is currently shown to the user, either as the selected rail item or as the
/// active document. Resource is the file the entry presents, and is empty for a rail item that has none.
/// AllowedAreas are the areas the entry may occupy, which Area is always one of.
/// </summary>
public record class UtilityInfo(
    EditorId UtilityId,
    string DisplayName,
    WorkspaceArea Area,
    IReadOnlyList<WorkspaceArea> AllowedAreas,
    bool IsShown,
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
