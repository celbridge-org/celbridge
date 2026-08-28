using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// A single entry in a utilities state snapshot: one button on the Utility Panel rail, whatever it presents.
/// IsShown is true when it is currently surfaced to the user, either as the active rail surface or as the
/// active document. Resource is the file the entry presents, and is empty for a rail surface that has none.
/// </summary>
public record class UtilityInfo(
    EditorId UtilityId,
    string DisplayName,
    WorkspaceArea Area,
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
