using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// A single utility in the UtilitiesStateSnapshot: a built-in Utility Panel surface (Explorer, Search) or a
/// package-contributed utility. Location is its current dock location, a document tab or the Utility Panel rail
/// (always UtilityPanel for the non-dockable built-ins). IsShown is whether it is currently surfaced to the
/// user: the active rail surface when in the panel, or the active document when docked as a document.
/// </summary>
public record class UtilityInfo(
    EditorInstanceId UtilityId,
    string DisplayName,
    DockLocation Location,
    bool IsShown);

/// <summary>
/// Snapshot of every available utility (built-in and custom) produced by IGetUtilitiesStateCommand.
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
