using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Logging;
using Windows.ApplicationModel.DataTransfer;

namespace Celbridge.Workspace.Commands;

public class CopyResourceToClipboardCommand : CommandBase, ICopyResourceToClipboardCommand
{
    public List<ResourceKey> SourceResources { get; set; } = new();
    public DataTransferMode TransferMode { get; set; }

    private readonly ILogger<CopyResourceToClipboardCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public CopyResourceToClipboardCommand(
        ILogger<CopyResourceToClipboardCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (SourceResources.Count == 0)
        {
            return Result.Ok();
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var storageItems = new List<IStorageItem>();

        foreach (var sourceResource in SourceResources)
        {
            var getResult = resourceRegistry.GetResource(sourceResource);
            if (getResult.IsFailure)
            {
                _logger.LogWarning($"Skipping resource '{sourceResource}' during clipboard copy: {getResult.Error}");
                continue;
            }
            var resource = getResult.Value;

            if (resource is IFileResource fileResource)
            {
                var filePath = resourceRegistry.GetResourcePath(fileResource);
                if (!string.IsNullOrEmpty(filePath))
                {
                    var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                    if (storageFile != null)
                    {
                        storageItems.Add(storageFile);
                    }
                }
            }
            else if (resource is IFolderResource folderResource)
            {
                var folderPath = resourceRegistry.GetResourcePath(folderResource);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    var storageFolder = await StorageFolder.GetFolderFromPathAsync(folderPath);
                    if (storageFolder != null)
                    {
                        storageItems.Add(storageFolder);
                    }
                }
            }
        }

        if (storageItems.Count == 0)
        {
            // Nothing to copy, treat it as a noop.
            return Result.Ok();
        }

        var dataPackage = new DataPackage();
        dataPackage.RequestedOperation = TransferMode == DataTransferMode.Copy ? DataPackageOperation.Copy : DataPackageOperation.Move;

        dataPackage.SetStorageItems(storageItems);
        Clipboard.SetContent(dataPackage);
        Clipboard.Flush();

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void CopyResourceToClipboard(ResourceKey resource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = [resource];
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public static void CopyResourcesToClipboard(List<ResourceKey> resources)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resources;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public static void CutResourceToClipboard(ResourceKey resource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = [resource];
            command.TransferMode = DataTransferMode.Move;
        });
    }

    public static void CutResourcesToClipboard(List<ResourceKey> resources)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resources;
            command.TransferMode = DataTransferMode.Move;
        });
    }
}
