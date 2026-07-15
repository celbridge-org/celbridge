using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents;

/// <summary>
/// Docks a utility at a dock location: reparents its single persistent WebView to the location's container
/// (the Utility Panel rail or a document tab), reusing the same WebView instance rather than recreating it.
/// </summary>
public interface IDockUtilityCommand : IExecutableCommand
{
    /// <summary>
    /// The id of the utility to dock.
    /// </summary>
    UtilityId UtilityId { get; set; }

    /// <summary>
    /// The dock location to move the utility to.
    /// </summary>
    DockLocation Location { get; set; }
}
