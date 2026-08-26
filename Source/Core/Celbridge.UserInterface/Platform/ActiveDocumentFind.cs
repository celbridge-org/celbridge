using Celbridge.Documents;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Resolves the find affordance the macOS host find paths act on: the Find menu item and the native key
/// monitor that delivers Command+F. Both drive the same document, so they resolve it the same way.
/// </summary>
internal static class ActiveDocumentFind
{
    /// <summary>
    /// The active document's find affordance, or null when no workspace is loaded or the active document
    /// offers no find of its own.
    /// </summary>
    public static IFindableDocument? GetActiveFindableDocument()
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspaceLoaded)
        {
            return null;
        }

        return workspaceWrapper.WorkspaceService.DocumentsService.GetActiveFindableDocument();
    }
}
