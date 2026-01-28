using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Services;

/// <summary>
/// Service for creating and executing resource transfer operations.
/// </summary>
public class ResourceTransferService : IResourceTransferService
{
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    
    private IResourceRegistry? _resourceRegistry;
    private IResourceRegistry ResourceRegistry => 
        _resourceRegistry ??= _workspaceWrapper.WorkspaceService.ResourceService.Registry;

    public ResourceTransferService(
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
    }

    public Result<IResourceTransfer> CreateResourceTransfer(List<string> sourcePaths, ResourceKey destFolderResource, DataTransferMode transferMode)
    {
        var createItemsResult = CreateResourceTransferItems(sourcePaths, destFolderResource);
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

    private Result<List<ResourceTransferItem>> CreateResourceTransferItems(List<string> sourcePaths, ResourceKey destFolderResource)
    {
        try
        {
            List<ResourceTransferItem> transferItems = new();

            var destFolderPath = ResourceRegistry.GetResourcePath(destFolderResource);
            if (!Directory.Exists(destFolderPath))
            {
                return Result<List<ResourceTransferItem>>.Fail($"The path '{destFolderPath}' does not exist.");
            }

            foreach (var sourcePath in sourcePaths)
            {
                if (PathContainsSubPath(destFolderPath, sourcePath) &&
                    string.Compare(destFolderPath, sourcePath, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    // Ignore attempts to transfer a resource into a subfolder of itself.
                    // This check is case insensitive to err on the safe side for Windows file systems.
                    // Without this check, a transfer operation could generate thousands of nested folders!
                    // It is ok to "transfer" a resource to the same path however as this indicates a duplicate operation.
                    return Result<List<ResourceTransferItem>>.Fail($"Cannot transfer a resource into a subfolder of itself.");
                }

                ResourceType resourceType = ResourceType.Invalid;
                if (File.Exists(sourcePath))
                {
                    resourceType = ResourceType.File;
                }
                else if (Directory.Exists(sourcePath))
                {
                    resourceType = ResourceType.Folder;
                }
                else
                {
                    // Resource does not exist in the file system, ignore it.
                    continue;
                }

                var getKeyResult = ResourceRegistry.GetResourceKey(sourcePath);
                if (getKeyResult.IsSuccess)
                {
                    // This resource is inside the project folder so we should use the CopyResource command
                    // to copy/move it so that the resource meta data is preserved.
                    // This is indicated by having a non-empty source resource property.

                    var sourceResource = getKeyResult.Value;

                    // Sanity check that the generated sourceResource matches the original source path
                    var checkSourcePath = ResourceRegistry.GetResourcePath(sourceResource);
                    Guard.IsTrue(sourcePath == checkSourcePath);

                    var destResource = ResourceRegistry.ResolveDestinationResource(sourceResource, destFolderResource);

                    var item = new ResourceTransferItem(resourceType, sourcePath, sourceResource, destResource);
                    transferItems.Add(item);
                }
                else
                {
                    // This file or folder resource is outside the project folder, so we should add it to the project
                    // via the AddResource command, which will create new metadata for the resource.
                    // This behaviour is indicated by having an empty source resource property.
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

    private static bool PathContainsSubPath(string path, string subPath)
    {
        string pathA = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string pathB = Path.GetFullPath(subPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return pathA.StartsWith(pathB, StringComparison.OrdinalIgnoreCase);
    }
}
