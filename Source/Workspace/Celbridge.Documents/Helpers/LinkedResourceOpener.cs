using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Workspace;

namespace Celbridge.Documents.Helpers;

/// <summary>
/// Opens the project resource a link in a document leads to. A file with an editor opens in it, and anything
/// else is selected in the Explorer, which is also how a linked folder is shown.
/// </summary>
public static class LinkedResourceOpener
{
    /// <summary>
    /// Opens the resource under its name on disk, since a link can differ from it in case. Returns false when
    /// the project has no such resource, leaving the caller to report the broken link.
    /// </summary>
    public static bool Open(ICommandService commandService, IWorkspaceService workspaceService, ResourceKey resource)
    {
        var normalizeResult = workspaceService.ResourceService.Registry.NormalizeResourceKey(resource);
        if (normalizeResult.IsFailure)
        {
            return false;
        }
        var linkedResource = normalizeResult.Value;

        if (workspaceService.DocumentsService.IsDocumentSupported(linkedResource))
        {
            commandService.Execute<IOpenDocumentCommand>(command =>
            {
                command.FileResource = linkedResource;
            });

            return true;
        }

        commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = linkedResource;
        });

        return true;
    }
}
