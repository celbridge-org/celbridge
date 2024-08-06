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

    public async Task<Result<IResourceTransfer>> GetClipboardResourceTransfer(ResourceKey destFolderResource)
    {
        if (GetClipboardContentType() != ClipboardContentType.Resource)
        {
            return Result<IResourceTransfer>.Fail("Clipboard content does not contain a resource");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(destFolderResource);
        if (getResult.IsFailure)
        {
            return Result<IResourceTransfer>.Fail($"Destination folder resource '{destFolderResource}' does not exist");
        }

        var resource = getResult.Value;
        if (resource is not IFolderResource)
        {
            return Result<IResourceTransfer>.Fail($"Resource '{destFolderResource}' is not a folder resource");
        }

        var destFolderPath = resourceRegistry.GetResourcePath(resource);
        if (!Directory.Exists(destFolderPath))
        {
            return Result<IResourceTransfer>.Fail($"The path '{destFolderPath}' does not exist.");
        }

        var transfer = new ResourceTransfer();

        var dataPackageView = DataTransfer.Clipboard.GetContent();

        // Note whether the operation is a move or a copy
        transfer.TransferMode = 
            dataPackageView.RequestedOperation == DataTransfer.DataPackageOperation.Move 
            ? ResourceTransferMode.Move 
            : ResourceTransferMode.Copy;

        try
        {
            var storageItems = await dataPackageView.GetStorageItemsAsync();

            // Todo: Make a utility for converting a set of StorageItems into a ResourceTransfer object
            // Convert to a list of paths first

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
                    var transferItem = new ResourceTransferItem(resourceType, sourcePath, sourceResource, destResource);

                    transfer.TransferItems.Add(transferItem);
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
                        var item = new ResourceTransferItem(resourceType, sourcePath, sourceResource, destResource);
                        transfer.TransferItems.Add(item);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Result<IResourceTransfer>.Fail($"Failed to generate clipboard resource description. {ex}");
        }

        return Result<IResourceTransfer>.Ok(transfer);
    }

    public async Task<Result> PasteClipboardResources(ResourceKey destFolderResource)
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Failed to paste resource items because no workspace is loaded");
        }

        var getResult = await GetClipboardResourceTransfer(destFolderResource);
        if (getResult.IsFailure)
        {
            return Result.Fail(getResult.Error);
        }
        var description = getResult.Value;

        if (description.TransferItems.Count == 1 &&
            description.TransferMode == ResourceTransferMode.Copy)
        {
            // If the source and destination resource are the same, display the duplicate
            // resource dialog instead of pasting the item.
            var clipboardResource = description.TransferItems[0]!;
            if (clipboardResource.SourceResource == clipboardResource.DestResource)
            {
                _commandService.Execute<IDuplicateResourceDialogCommand>(command =>
                {
                    command.Resource = clipboardResource.SourceResource;
                });
                return Result.Ok();
            }
        }

        var projectService = _workspaceWrapper.WorkspaceService.ProjectService;
        return await projectService.TransferResources(destFolderResource, description);
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
