using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Explorer;
using ApplicationDataTransfer = Windows.ApplicationModel.DataTransfer;

namespace Celbridge.WorkspaceUI.Services;

public class DataTransferService : IDataTransferService, IDisposable
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFileClipboard _fileClipboard;

    public DataTransferService(
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IFileClipboard fileClipboard)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _messengerService = messengerService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _fileClipboard = fileClipboard;

        ApplicationDataTransfer.Clipboard.ContentChanged += Clipboard_ContentChanged;
    }

    private void Clipboard_ContentChanged(object? sender, object e)
    {
        var message = new ClipboardContentChangedMessage();
        _messengerService.Send(message);
    }

    public ClipboardContentDescription GetClipboardContentDescription()
    {
        // Files are handled by the platform file clipboard (NSPasteboard on macOS); text stays on the
        // WinRT clipboard, which round-trips on every head.
        var fileTransferMode = _fileClipboard.GetFileTransferMode();
        if (fileTransferMode is not null)
        {
            var fileOperation = fileTransferMode == DataTransferMode.Move
                ? ClipboardContentOperation.Move
                : ClipboardContentOperation.Copy;
            return new ClipboardContentDescription(ClipboardContentType.Resource, fileOperation);
        }

        var dataPackageView = ApplicationDataTransfer.Clipboard.GetContent();
        if (dataPackageView.Contains(ApplicationDataTransfer.StandardDataFormats.Text))
        {
            return new ClipboardContentDescription(ClipboardContentType.Text, MapContentOperation(dataPackageView.RequestedOperation));
        }

        return new ClipboardContentDescription(ClipboardContentType.None, ClipboardContentOperation.None);
    }

    private static ClipboardContentOperation MapContentOperation(ApplicationDataTransfer.DataPackageOperation operation)
    {
        return operation switch
        {
            ApplicationDataTransfer.DataPackageOperation.Copy => ClipboardContentOperation.Copy,
            ApplicationDataTransfer.DataPackageOperation.Move => ClipboardContentOperation.Move,
            _ => ClipboardContentOperation.None
        };
    }

    public async Task<Result<IResourceTransfer>> GetClipboardResourceTransfer(ResourceKey destFolderResource)
    {
        // Read the files (and their copy/move mode) from the platform file clipboard once.
        var clipboardFiles = await _fileClipboard.GetFilesAsync();
        if (clipboardFiles is null)
        {
            return Result<IResourceTransfer>.Fail("Clipboard content does not contain a resource");
        }

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result<IResourceTransfer>.Fail("Workspace is not loaded");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceTransferService = _workspaceWrapper.WorkspaceService.ResourceService.Transfers;

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

        var resolveResult = resourceRegistry.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result<IResourceTransfer>.Fail($"Failed to resolve path for resource: '{destFolderResource}'")
                .WithErrors(resolveResult);
        }
        var destFolderPath = resolveResult.Value;

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var destInfoResult = await resourceFileSystem.GetInfoAsync(destFolderResource);
        if (destInfoResult.IsFailure
            || destInfoResult.Value.Kind != StorageItemKind.Folder)
        {
            return Result<IResourceTransfer>.Fail($"The path '{destFolderPath}' does not exist.");
        }

        try
        {
            var paths = new List<string>();
            foreach (var filePath in clipboardFiles.Paths)
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    continue;
                }

                paths.Add(Path.GetFullPath(filePath));
            }

            if (paths.Count == 0)
            {
                return Result<IResourceTransfer>.Fail("No valid file paths found in clipboard");
            }

            var transferMode = clipboardFiles.TransferMode;

            var createTransferResult = await resourceTransferService.CreateResourceTransferAsync(paths, destFolderResource, transferMode);
            if (createTransferResult.IsFailure)
            {
                return Result<IResourceTransfer>.Fail($"Failed to create resource transfer.")
                    .WithErrors(createTransferResult);
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
            return Result.Fail("Failed to get clipboard resource transfer")
                .WithErrors(getResult);
        }
        var description = getResult.Value;

        if (description.TransferItems.Count == 1 &&
            description.TransferMode == DataTransferMode.Copy)
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

        var resourceTransferService = _workspaceWrapper.WorkspaceService.ResourceService.Transfers;
        return resourceTransferService.TransferResources(destFolderResource, description);
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
