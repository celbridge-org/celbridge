using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// Docks a utility in a workspace area, reparenting its single persistent WebView to that area's
/// container (the Utility Panel rail or a document tab) rather than recreating it.
/// </summary>
public interface IDockUtilityCommand : IExecutableCommand
{
    /// <summary>
    /// The id of the utility to dock.
    /// </summary>
    EditorId UtilityId { get; set; }

    /// <summary>
    /// The workspace area to move the utility to.
    /// </summary>
    WorkspaceArea Area { get; set; }
}
