using Celbridge.BaseLibrary.Clipboard;
using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Celbridge.Workspace.Services;

public class ClipboardService : IClipboardService
{
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;

    public ClipboardService(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _commandService = commandService;
    }

    public ClipboardContentType GetClipboardContentType()
    {
        var dataPackageView = Clipboard.GetContent();
        if (dataPackageView.Contains(StandardDataFormats.StorageItems))
        {
            return ClipboardContentType.Resource;
        }
        else if (dataPackageView.Contains(StandardDataFormats.Text))
        {
            return ClipboardContentType.Text;
        }

        return ClipboardContentType.None;
    }

    public async Task<Result<IClipboardResourcesDescription>> GetClipboardResourceDescription(ResourceKey destFolderResource)
    {
        if (GetClipboardContentType() != ClipboardContentType.Resource)
        {
            return Result<IClipboardResourcesDescription>.Fail("Clipboard content does not contain a resource");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(destFolderResource);
        if (getResult.IsFailure)
        {
            return Result<IClipboardResourcesDescription>.Fail($"Destination folder resource '{destFolderResource}' does not exist");
        }

        var resource = getResult.Value;
        if (resource is not IFolderResource)
        {
            return Result<IClipboardResourcesDescription>.Fail($"Resource '{destFolderResource}' is not a folder resource");
        }

        var destFolderPath = resourceRegistry.GetResourcePath(resource);
        if (!Directory.Exists(destFolderPath))
        {
            return Result<IClipboardResourcesDescription>.Fail($"The path '{destFolderPath}' does not exist.");
        }

        var description = new ClipboardResourceDescription();

        var dataPackageView = Clipboard.GetContent();

        // Note whether the operation is a move or a copy
        description.Operation = 
            dataPackageView.RequestedOperation == DataPackageOperation.Move 
            ? CopyResourceOperation.Move 
            : CopyResourceOperation.Copy;

        try
        {
            var storageItems = await dataPackageView.GetStorageItemsAsync();

            foreach (var storageItem in storageItems)
            {
                var storageItemPath = storageItem.Path;

                if (PathContainsSubPath(destFolderPath, storageItemPath) &&
                    string.Compare(destFolderPath, storageItemPath, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    // Ignore attempts to paste a resource into a subfolder of itself.
                    // This check is case insensitive to err on the safe side for Windows file systems.
                    // Without this check, a paste operation can generate thousands of nested folders!
                    continue;
                }

                var resourceType = storageItem is StorageFile ? ResourceType.File : ResourceType.Folder;

                var getKeyResult = resourceRegistry.GetResourceKey(storageItemPath);
                if (getKeyResult.IsSuccess)
                {
                    var sourceResource = getKeyResult.Value;
                    var sourcePath = resourceRegistry.GetResourcePath(sourceResource);
                    var destResource = resourceRegistry.ResolveDestinationResource(sourceResource, destFolderResource);

                    // This resource is inside the project folder so we can use the CopyResource command
                    // to copy/move it so that the resource meta data is preserved.
                    // This is indicated by having a non-empty source resource property.
                    var item = new ClipboardResourceItem(resourceType, sourcePath, sourceResource, destResource);

                    description.ResourceItems.Add(item);
                }
                else
                {
                    if (storageItem is StorageFile file)
                    {
                        var sourcePath = file.Path;
                        var sourceResource = new ResourceKey();
                        var destResource = destFolderResource.Combine(file.Name);

                        // This resource is outside the project folder, so it should be added to the project via the
                        // AddResource command, which will create new metadata for the resource.
                        // This is indicated by having an empty source resource property.
                        var item = new ClipboardResourceItem(resourceType, sourcePath, sourceResource, destResource);
                        description.ResourceItems.Add(item);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Result<IClipboardResourcesDescription>.Fail($"Failed to generate clipboard resource description. {ex}");
        }

        return Result<IClipboardResourcesDescription>.Ok(description);
    }

    public async Task<Result> PasteResourceItems(ResourceKey destFolderResource)
    {
        var getResult = await GetClipboardResourceDescription(destFolderResource);
        if (getResult.IsFailure)
        {
            return Result.Fail(getResult.Error);
        }
        var description = getResult.Value;

        if (description.ResourceItems.Count == 1 &&
            description.Operation == CopyResourceOperation.Copy)
        {
            // If the source and destination resource are the same, display the duplicate
            // resource dialog instead of pasting the item.
            var clipboardResource = description.ResourceItems[0]!;
            if (clipboardResource.SourceResource == clipboardResource.DestResource)
            {
                _commandService.Execute<IShowDuplicateResourceDialogCommand>(command =>
                {
                    command.Resource = clipboardResource.SourceResource;
                });
                return Result.Ok();
            }
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

        // Filter out any items where the destination resource already exists
        // Todo: If it's a single item, ask the user if they want to replace the existing resource
        description.ResourceItems.RemoveAll(item =>
        {
            return resourceRegistry.GetResource(item.DestResource).IsSuccess;
        });

        if (description.ResourceItems.Count == 0)
        {
            // All resource items have been filtered out so nothing left to paste
            return Result.Ok();
        }

        foreach (var resourceItem in description.ResourceItems)
        {
            if (resourceItem.SourceResource.IsEmpty)
            {
                // This is a resource outside the project folder, add it using the AddResource command.
                _commandService.Execute<IAddResourceCommand>(command =>
                {
                    command.ResourceType = resourceItem.ResourceType;
                    command.DestResource = resourceItem.DestResource;
                    command.SourcePath = resourceItem.SourcePath;
                });
            }
            else
            { 
                // This is a resource inside the project folder, copy/move it using the CopyResource command.
                _commandService.Execute<ICopyResourceCommand>(command =>
                {
                    command.SourceResource = resourceItem.SourceResource;
                    command.DestResource = resourceItem.DestResource;
                    command.Operation = description.Operation;
                });
            }
        }

        resourceRegistry.SetFolderIsExpanded(destFolderResource, true);

        var message = new RequestResourceTreeUpdateMessage();
        _messengerService.Send(message);

        return Result.Ok();
    }

    private bool PathContainsSubPath(string path, string subPath)
    {
        string pathA = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string pathB = Path.GetFullPath(subPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return pathA.StartsWith(pathB, StringComparison.OrdinalIgnoreCase);
    }
}
