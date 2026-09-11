using Celbridge.Documents;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Resolves the active document for the macOS host paths that act on it: the Find menu item and the native
/// key monitor that delivers Command+F. Reaching the workspace service throws when no project is open, so
/// both go through here rather than guarding separately, and both resolve the same document.
/// </summary>
internal static class ActiveDocumentResolver
{
    /// <summary>
    /// The active document's view, or null while no workspace is loaded.
    /// </summary>
    public static IDocumentView? GetActiveDocumentView()
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();

        // IsWorkspaceLoaded, not HasWorkspaceService: the wider signal is already true while the workspace
        // is loading, and the host offers no find until the load has finished.
        if (!workspaceWrapper.IsWorkspaceLoaded)
        {
            return null;
        }

        return workspaceWrapper.WorkspaceService.DocumentsService.GetActiveDocumentView();
    }
}
