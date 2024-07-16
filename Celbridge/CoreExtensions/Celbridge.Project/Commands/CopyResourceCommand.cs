using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Celbridge.Project.Commands;

public class CopyResourceCommand : CommandBase, ICopyResourceCommand
{
    public override string UndoStackName => UndoStackNames.None;

    public ResourceKey ResourceKey { get; set; }

    public bool Move { get; set; }

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public CopyResourceCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(ResourceKey);
        if (getResult.IsFailure)
        {
            return getResult;
        }
        var resource = getResult.Value;

        var storageItems = new List<IStorageItem>();

        if (resource is IFileResource fileResource)
        {
            var filePath = resourceRegistry.GetPath(fileResource);
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
            var folderPath = resourceRegistry.GetPath(folderResource);
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
        dataPackage.RequestedOperation = Move ? DataPackageOperation.Move : DataPackageOperation.Copy;

        dataPackage.SetStorageItems(storageItems);
        Clipboard.SetContent(dataPackage);
        Clipboard.Flush();

        return Result.Ok();
    }

    public static void CutResource(string resourceKey)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ICopyResourceCommand>(command =>
        {
            command.ResourceKey = resourceKey;
            command.Move = true;
        });
    }

    public static void CopyResource(string resourceKey)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ICopyResourceCommand>(command => command.ResourceKey = resourceKey);
    }
}
