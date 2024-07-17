using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using Microsoft.Extensions.Localization;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Celbridge.Project.Commands;

public class PasteResourceCommand : CommandBase, IPasteResourceCommand
{
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;

    public override string UndoStackName => UndoStackNames.None;

    public ResourceKey FolderResourceKey { get; set; }

    public PasteResourceCommand(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var pasteResult = await PasteAsync();
        if (pasteResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_Paste");
            var messageString = _stringLocalizer.GetString("ResourceTree_PasteResourceFailed", FolderResourceKey.ToString());

            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return pasteResult;
    }

    private async Task<Result> PasteAsync()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(FolderResourceKey);
        if (getResult.IsFailure)
        {
            return getResult;
        }
        var folderResource = getResult.Value as IFolderResource;
        if (folderResource is null)
        {
            return Result.Fail($"Failed to paste resource. '{FolderResourceKey}' is not a valid folder.");
        }

        var pasteFolderPath = resourceRegistry.GetPath(folderResource);
        if (!Directory.Exists(pasteFolderPath))
        {
            return Result.Fail($"Failed to paste resource. The '{pasteFolderPath}' path does not exist.");
        }

        var dataPackageView = Clipboard.GetContent();

        bool isMove = dataPackageView.RequestedOperation == DataPackageOperation.Move;

        bool modified = false;
        if (dataPackageView.Contains(StandardDataFormats.StorageItems))
        {
            var pasteFolder = await StorageFolder.GetFolderFromPathAsync(pasteFolderPath);
            var storageItems = await dataPackageView.GetStorageItemsAsync();
            try
            {
                foreach (var storageItem in storageItems)
                {
                    if (storageItem is StorageFile storageFile)
                    {
                        await CopyStorageFileAsync(storageFile, pasteFolder);
                        if (isMove)
                        {
                            // For some unknown reason, using storageFile.DeleteAsync() here throws a readonly
                            // file exception so we use File.Delete() instead.
                            File.Delete(storageFile.Path);
                        }

                        modified = true;
                    }
                    else if (storageItem is StorageFolder storageFolder)
                    {
                        if (PathContainsSubPath(pasteFolder.Path, storageFolder.Path))
                        {
                            return Result.Fail($"Failed to paste resources. A folder cannot be pasted into a subfolder of itself.");
                        }

                        await CopyStorageFolderAsync(storageFolder, pasteFolder);
                        if (isMove)
                        {
                            // For some unknown reason, using storageFolder.DeleteAsync() here throws a readonly
                            // exception so we use Directory.Delete() instead.
                            Directory.Delete(storageFolder.Path, true);
                        }

                        modified = true;
                    }
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to paste resources. {ex}");
            }
        }

        if (!modified)
        {
            return Result.Fail("Failed to paste resources. No resources found in clipboard.");
        }

        resourceRegistry.SetFolderIsExpanded(FolderResourceKey, true);

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        return Result.Ok();
    }

    private async Task CopyStorageFileAsync(StorageFile sourceFile, StorageFolder destinationFolder)
    {
        await sourceFile.CopyAsync(destinationFolder, sourceFile.Name, NameCollisionOption.FailIfExists);
    }

    private async Task CopyStorageFolderAsync(StorageFolder sourceFolder, StorageFolder destinationFolder)
    {
        if (sourceFolder == null || destinationFolder == null)
        {
            throw new ArgumentNullException("Source or destination folder is null.");
        }

        var destinationSubFolder = await destinationFolder.CreateFolderAsync(sourceFolder.Name, CreationCollisionOption.FailIfExists);

        var files = await sourceFolder.GetFilesAsync();
        foreach (var file in files)
        {
            await CopyStorageFileAsync(file, destinationSubFolder);
        }

        var subFolders = await sourceFolder.GetFoldersAsync();
        foreach (var subFolder in subFolders)
        {
            await CopyStorageFolderAsync(subFolder, destinationSubFolder);
        }
    }

    private bool PathContainsSubPath(string path, string subPath)
    {
        string pathA = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string pathB = Path.GetFullPath(subPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return pathA.StartsWith(pathB, StringComparison.OrdinalIgnoreCase);
    }

    //
    // Static methods for scripting support.
    //

    public static void PasteResource(ResourceKey folderResourceKey)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IPasteResourceCommand>(command => command.FolderResourceKey = folderResourceKey);
    }
}
