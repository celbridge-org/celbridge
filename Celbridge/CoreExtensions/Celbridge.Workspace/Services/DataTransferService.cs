using Celbridge.DataTransfer;
using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Resources;

using ApplicationDataTransfer = Windows.ApplicationModel.DataTransfer;

namespace Celbridge.Workspace.Services;

public class DataTransferService : IDataTransferService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;

    public DataTransferService(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _commandService = commandService;

        ApplicationDataTransfer.Clipboard.ContentChanged += Clipboard_ContentChanged;
    }

    private void Clipboard_ContentChanged(object? sender, object e)
    {
        var message = new ClipboardContentChangedMessage();
        _messengerService.Send(message);
    }

    public ClipboardContentType GetClipboardContentType()
    {
        var dataPackageView = ApplicationDataTransfer.Clipboard.GetContent();
        if (dataPackageView.Contains(ApplicationDataTransfer.StandardDataFormats.StorageItems))
        {
            return ClipboardContentType.Resource;
        }
        else if (dataPackageView.Contains(ApplicationDataTransfer.StandardDataFormats.Text))
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

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result<IResourceTransfer>.Fail("Workspace is not loaded");
        }

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var resourceRegistry = resourceService.ResourceRegistry;

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

        var dataPackageView = ApplicationDataTransfer.Clipboard.GetContent();

        try
        {
            var storageItems = await dataPackageView.GetStorageItemsAsync();
            var paths = new List<string>();
            foreach (var storageItem in storageItems)
            {
                var path = Path.GetFullPath(storageItem.Path);
                paths.Add(path);
            }

            // Note whether the operation is a move or a copy
            var transferMode =
                dataPackageView.RequestedOperation == ApplicationDataTransfer.DataPackageOperation.Move
                ? ResourceTransferMode.Move
                : ResourceTransferMode.Copy;

            var createTransferResult = resourceService.CreateResourceTransfer(paths, destFolderResource, transferMode);
            if (createTransferResult.IsFailure)
            {
                return Result<IResourceTransfer>.Fail($"Failed to create resource transfer. {createTransferResult.Error}");
            }
            var resourceTransfer = createTransferResult.Value;

            return Result<IResourceTransfer>.Ok(resourceTransfer);
        }
        catch (Exception ex)
        {
            return Result<IResourceTransfer>.Fail($"Failed to generate clipboard resource description. {ex}");
        }
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

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        return await resourceService.TransferResources(destFolderResource, description);
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
                ApplicationDataTransfer.Clipboard.ContentChanged -= Clipboard_ContentChanged;
            }

            _disposed = true;
        }
    }

    ~DataTransferService()
    {
        Dispose(false);
    }
}
