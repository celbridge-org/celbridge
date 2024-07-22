using Celbridge.Clipboard;
using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Resources;
using Windows.Storage;

using DataTransfer = Windows.ApplicationModel.DataTransfer;

namespace Celbridge.Workspace.Services;

public class ClipboardService : IClipboardService, IDisposable
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

        DataTransfer.Clipboard.ContentChanged += Clipboard_ContentChanged;
    }

    private void Clipboard_ContentChanged(object? sender, object e)
    {
        var message = new ClipboardContentChangedMessage();
        _messengerService.Send(message);
    }

    public ClipboardContentType GetClipboardContentType()
    {
        var dataPackageView = DataTransfer.Clipboard.GetContent();
        if (dataPackageView.Contains(DataTransfer.StandardDataFormats.StorageItems))
        {
            return ClipboardContentType.Resource;
        }
        else if (dataPackageView.Contains(DataTransfer.StandardDataFormats.Text))
        {
            return ClipboardContentType.Text;
        }

        return ClipboardContentType.None;
    }

    public async Task<Result<IClipboardResourceContent>> GetClipboardResourceContent(ResourceKey destFolderResource)
    {
        if (GetClipboardContentType() != ClipboardContentType.Resource)
        {
            return Result<IClipboardResourceContent>.Fail("Clipboard content does not contain a resource");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(destFolderResource);
        if (getResult.IsFailure)
        {
            return Result<IClipboardResourceContent>.Fail($"Destination folder resource '{destFolderResource}' does not exist");
        }

        var resource = getResult.Value;
        if (resource is not IFolderResource)
        {
            return Result<IClipboardResourceContent>.Fail($"Resource '{destFolderResource}' is not a folder resource");
        }

        var destFolderPath = resourceRegistry.GetResourcePath(resource);
        if (!Directory.Exists(destFolderPath))
        {
            return Result<IClipboardResourceContent>.Fail($"The path '{destFolderPath}' does not exist.");
        }

        var description = new ClipboardResourceDescription();

        var dataPackageView = DataTransfer.Clipboard.GetContent();

        // Note whether the operation is a move or a copy
        description.Operation = 
            dataPackageView.RequestedOperation == DataTransfer.DataPackageOperation.Move 
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
                    var destResource = resourceRegistry.GetCopyDestinationResource(sourceResource, destFolderResource);

                    // This resource is inside the project folder so we should use the CopyResource command
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

                        // This resource is outside the project folder, so we should add it to the project
                        // via the AddResource command, which will create new metadata for the resource.
                        // This is indicated by having an empty source resource property.
                        var item = new ClipboardResourceItem(resourceType, sourcePath, sourceResource, destResource);
                        description.ResourceItems.Add(item);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Result<IClipboardResourceContent>.Fail($"Failed to generate clipboard resource description. {ex}");
        }

        return Result<IClipboardResourceContent>.Ok(description);
    }

    public async Task<Result> PasteResourceItems(ResourceKey destFolderResource)
    {
        var getResult = await GetClipboardResourceContent(destFolderResource);
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
                _commandService.Execute<IDuplicateResourceDialogCommand>(command =>
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
                // This resource is outside the project folder, add it using the AddResource command.
                _commandService.Execute<IAddResourceCommand>(command =>
                {
                    command.ResourceType = resourceItem.ResourceType;
                    command.DestResource = resourceItem.DestResource;
                    command.SourcePath = resourceItem.SourcePath;
                });
            }
            else
            { 
                // This resource is inside the project folder, copy/move it using the CopyResource command.
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

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
                DataTransfer.Clipboard.ContentChanged -= Clipboard_ContentChanged;
            }

            _disposed = true;
        }
    }

    ~ClipboardService()
    {
        Dispose(false);
    }
}
