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
    /// Opens the resource the link names. Fails when the project has no such resource, or when the link differs
    /// from the resource's stored name. The caller then reports the broken link. The failure for a name that
    /// differs carries a StoredNameMismatchError.
    /// </summary>
    public static async Task<Result> OpenAsync(ICommandService commandService, IWorkspaceService workspaceService, ResourceKey resource)
    {
        var infoResult = await workspaceService.ResourceService.FileSystem.GetInfoAsync(resource);
        if (infoResult.IsFailure)
        {
            return Result.Fail($"Failed to find the linked resource: '{resource}'")
                .WithErrors(infoResult);
        }

        if (infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Fail($"The linked resource is not in the project: '{resource}'");
        }

        if (workspaceService.DocumentsService.IsDocumentSupported(resource))
        {
            commandService.Execute<IOpenDocumentCommand>(command =>
            {
                command.FileResource = resource;
            });

            return Result.Ok();
        }

        commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = resource;
        });

        return Result.Ok();
    }
}
