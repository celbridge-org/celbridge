using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// Reveals a rail item by its fully-qualified id, optionally moving it to a workspace area first. Explorer
/// and Search select their rail item, a contributed utility is revealed wherever it lives, and a document
/// shortcut opens its document.
/// </summary>
public interface IShowUtilityCommand : IExecutableCommand
{
    /// <summary>
    /// The id of the utility to reveal: a built-in id (e.g. "celbridge.explorer") or a custom id.
    /// </summary>
    EditorId UtilityId { get; set; }

    /// <summary>
    /// The area to move the utility to before revealing it. Null reveals it wherever it currently is.
    /// Only a contributed utility moves between areas: for a built-in utility or a document shortcut,
    /// naming an area it cannot reach fails rather than being ignored.
    /// </summary>
    WorkspaceArea? TargetArea { get; set; }
}
