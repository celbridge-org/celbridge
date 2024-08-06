using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Workspace;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Celbridge.Projects.Commands;

public class CopyResourceToClipboardCommand : CommandBase, ICopyResourceToClipboardCommand
{
    public override string UndoStackName => UndoStackNames.None;

    public ResourceKey SourceResource { get; set; }
    public ResourceTransferMode TransferMode { get; set; }
  
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public CopyResourceToClipboardCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(SourceResource);
        if (getResult.IsFailure)
        {
            return getResult;
        }
        var resource = getResult.Value;

        var storageItems = new List<IStorageItem>();

        if (resource is IFileResource fileResource)
        {
            var filePath = resourceRegistry.GetResourcePath(fileResource);
            if (string.IsNullOrEmpty(filePath))
            {
                return Result.Fail($"Failed to get path for file resource '{fileResource}'");
            }

            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            if (storageFile != null)
            {
                storageItems.Add(storageFile);
            }
        }
        else if (resource is IFolderResource folderResource)
        {
            var folderPath = resourceRegistry.GetResourcePath(folderResource);
            if (string.IsNullOrEmpty(folderPath))
            {
                return Result.Fail($"Failed to get path for folder resource '{folderResource}'");
            }

            var storageFolder = await StorageFolder.GetFolderFromPathAsync(folderPath);
            if (storageFolder != null)
            {
                storageItems.Add(storageFolder);
            }
        }

        if (storageItems.Count == 0)
        {
            // Nothing to copy, treat it as a noop.
            return Result.Ok();
        }

        var dataPackage = new DataPackage();
        dataPackage.RequestedOperation = TransferMode == ResourceTransferMode.Copy ? DataPackageOperation.Copy : DataPackageOperation.Move;

        dataPackage.SetStorageItems(storageItems);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void CopyResourceToClipboard(ResourceKey resource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResource = resource;
            command.TransferMode = ResourceTransferMode.Copy;
        });
    }

    public static void CutResourceToClipboard(ResourceKey resource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResource = resource;
            command.TransferMode = ResourceTransferMode.Move;
        });
    }
}
