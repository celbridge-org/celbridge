using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Service for creating and executing resource transfer operations.
/// </summary>
public class ResourceTransferService : IResourceTransferService
{
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILocalFileSystem _fileSystem;

    private IRootHandlerRegistry RootHandlers =>
        _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

    public ResourceTransferService(
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        ILocalFileSystem fileSystem)
    {
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _fileSystem = fileSystem;
    }

    public async Task<Result<IResourceTransfer>> CreateResourceTransferAsync(List<string> sourcePaths, ResourceKey destFolderResource, DataTransferMode transferMode)
    {
        var createItemsResult = await CreateResourceTransferItemsAsync(sourcePaths, destFolderResource);
        if (createItemsResult.IsFailure)
        {
            return Result<IResourceTransfer>.Fail($"Failed to create resource transfer items.")
                .WithErrors(createItemsResult);
        }
        var transferItems = createItemsResult.Value;

        var resourceTransfer = new ResourceTransfer()
        {
            TransferMode = transferMode,
            TransferItems = transferItems
        };

        return Result<IResourceTransfer>.Ok(resourceTransfer);
    }

    private async Task<Result<List<ResourceTransferItem>>> CreateResourceTransferItemsAsync(List<string> sourcePaths, ResourceKey destFolderResource)
    {
        try
        {
            List<ResourceTransferItem> transferItems = new();

            var resolveResult = RootHandlers.ResolveResourcePath(destFolderResource);
            if (resolveResult.IsFailure)
            {
                return Result<List<ResourceTransferItem>>.Fail($"Failed to resolve path for resource: '{destFolderResource}'")
                    .WithErrors(resolveResult);
            }
            var destFolderPath = resolveResult.Value;

            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            var destInfoResult = await resourceFileSystem.GetInfoAsync(destFolderResource);
            if (destInfoResult.IsFailure
                || destInfoResult.Value.Kind != StorageItemKind.Folder)
            {
                return Result<List<ResourceTransferItem>>.Fail($"The path '{destFolderPath}' does not exist.");
            }

            foreach (var sourcePath in sourcePaths)
            {
                ResourceType resourceType = ResourceType.Invalid;
                var sourceInfoResult = await _fileSystem.GetInfoAsync(sourcePath);
                if (sourceInfoResult.IsSuccess
                    && sourceInfoResult.Value.Kind == StorageItemKind.File)
                {
                    resourceType = ResourceType.File;
                }
                else if (sourceInfoResult.IsSuccess
                    && sourceInfoResult.Value.Kind == StorageItemKind.Folder)
                {
                    resourceType = ResourceType.Folder;
                }
                else
                {
                    // Resource does not exist in the file system, ignore it.
                    continue;
                }

                var getKeyResult = RootHandlers.GetResourceKey(sourcePath);
                if (getKeyResult.IsSuccess)
                {
                    // This resource is inside the project folder so we should use the CopyResource command
                    // to copy/move it so that the resource meta data is preserved.
                    // This is indicated by having a non-empty source resource property.

                    var sourceResource = getKeyResult.Value;

                    // The key leads back to the path only for a resource. The root refuses to resolve the key of
                    // a link inside the project. The key of a path in another letter case resolves elsewhere.
                    var checkSourcePathResult = RootHandlers.ResolveResourcePath(sourceResource);
                    if (checkSourcePathResult.IsFailure)
                    {
                        return Result<List<ResourceTransferItem>>.Fail($"The path '{sourcePath}' is not a resource that can be transferred.")
                            .WithErrors(checkSourcePathResult);
                    }
                    if (!string.Equals(sourcePath, checkSourcePathResult.Value, StringComparison.Ordinal))
                    {
                        return Result<List<ResourceTransferItem>>.Fail(
                            $"The path '{sourcePath}' resolves to '{checkSourcePathResult.Value}', so it cannot be transferred.");
                    }

                    var destResource = await ResolveDestinationResourceAsync(sourceResource, destFolderResource);

                    var destinationResult = TransferRules.CheckDestination(sourceResource, destResource);
                    if (destinationResult.IsFailure)
                    {
                        return Result.Fail(destinationResult);
                    }

                    var item = new ResourceTransferItem(resourceType, sourcePath, sourceResource, destResource);
                    transferItems.Add(item);
                }
                else
                {
                    // This file or folder resource is outside the project folder, so we should add it to the project
                    // via the CreateResource command, which will create new metadata for the resource.
                    // This behaviour is indicated by having an empty source resource property.

                    // A folder outside the project that contains the destination would be copied into itself without
                    // end. The check ignores letter case, since Windows stores names that way.
                    if (PathContains(sourcePath, destFolderPath))
                    {
                        return Result<List<ResourceTransferItem>>.Fail($"Cannot transfer '{sourcePath}' into itself.");
                    }

                    var sourceResource = new ResourceKey();
                    var resourcename = Path.GetFileName(sourcePath);
                    var destResource = destFolderResource.Combine(resourcename);

                    var item = new ResourceTransferItem(resourceType, sourcePath, sourceResource, destResource);
                    transferItems.Add(item);
                }
            }

            if (transferItems.Count == 0)
            {
                return Result<List<ResourceTransferItem>>.Fail($"Transfer item list is empty.");
            }

            return Result<List<ResourceTransferItem>>.Ok(transferItems);
        }
        catch (Exception ex)
        {
            return Result<List<ResourceTransferItem>>.Fail($"Failed to create resource transfer items.")
                .WithException(ex);
        }
    }

    public Result TransferResources(ResourceKey destFolderResource, IResourceTransfer transfer)
    {
        // Uses Execute, not ExecuteAsync, to avoid deadlock when called from within another command.
        _commandService.Execute<ITransferResourcesCommand>(command =>
        {
            command.DestFolderResource = destFolderResource;
            command.TransferMode = transfer.TransferMode;
            command.TransferItems = transfer.TransferItems;
        });

        return Result.Ok();
    }

    public async Task<ResourceKey> ResolveDestinationResourceAsync(ResourceKey sourceResource, ResourceKey destResource)
    {
        string output = destResource;

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var infoResult = await resourceFileSystem.GetInfoAsync(destResource);
        if (infoResult.IsSuccess)
        {
            if (infoResult.Value.Kind == StorageItemKind.Folder)
            {
                if (destResource.IsEmpty)
                {
                    // Destination is the project folder.
                    output = sourceResource.ResourceName;
                }
                else
                {
                    if (sourceResource == destResource)
                    {
                        // Source and destination are the same folder. Allowed
                        // because the user may duplicate a folder by copying
                        // and pasting it to the same destination.
                        output = destResource;
                    }
                    else
                    {
                        // Destination is a folder, so append the source resource name to it.
                        output = destResource.Combine(sourceResource.ResourceName);
                    }
                }
            }
        }

        return output;
    }

    // Returns true if the path is the outer path or lies inside it. A sibling lies outside, even when the sibling's
    // name starts with the outer path's name.
    private static bool PathContains(string outerPath, string path)
    {
        var outer = Path.GetFullPath(outerPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var inner = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return inner.Equals(outer, StringComparison.OrdinalIgnoreCase)
            || inner.StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
