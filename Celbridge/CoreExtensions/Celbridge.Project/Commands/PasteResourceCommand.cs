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

    public override string StackName => CommandStackNames.Project;

    public ResourceKey FolderResourceKey { get; set; }

    private List<string> _pastedPaths = new();

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
            var messageString = _stringLocalizer.GetString("ResourceTree_PasteResourceFailed", FolderResourceKey);

            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return pasteResult;
    }

    public override async Task<Result> UndoAsync()
    {
        var pasteResult = await UndoPasteAsync();
        if (pasteResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_Paste");
            var messageString = _stringLocalizer.GetString("ResourceTree_UndoPasteResourceFailed", FolderResourceKey);

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

        var pasteFolder = await StorageFolder.GetFolderFromPathAsync(pasteFolderPath);
        var dataPackageView = Clipboard.GetContent();

        if (dataPackageView.Contains(StandardDataFormats.StorageItems))
        {
            var storageItems = await dataPackageView.GetStorageItemsAsync();
            try
            {
                foreach (var storageItem in storageItems)
                {
                    if (storageItem is StorageFile storageFile)
                    {
                        await CopyStorageFileAsync(storageFile, pasteFolder);
                    }
                    else if (storageItem is StorageFolder storageFolder)
                    {
                        await CopyStorageFolderAsync(storageFolder, pasteFolder);
                    }
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to paste resources. {ex}");
            }
        }

        if (_pastedPaths.Count == 0)
        {
            return Result.Fail("Failed to paste resources. No resources found in clipboard.");
        }

        resourceRegistry.SetFolderIsExpanded(FolderResourceKey, true);

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        return Result.Ok();
    }

    private async Task<Result> UndoPasteAsync()
    {
        foreach (var pastedPath in _pastedPaths)
        {
            try
            {
                if (File.Exists(pastedPath))
                {
                    File.Delete(pastedPath);
                }
                else if (Directory.Exists(pastedPath))
                {
                    Directory.Delete(pastedPath, true);
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to undo paste resources. {ex}");
            }
        }
        _pastedPaths.Clear();

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    private async Task CopyStorageFileAsync(StorageFile sourceFile, StorageFolder destinationFolder)
    {
        var newFile = await sourceFile.CopyAsync(destinationFolder, sourceFile.Name, NameCollisionOption.FailIfExists);
        _pastedPaths.Add(newFile.Path);
    }

    private async Task CopyStorageFolderAsync(StorageFolder sourceFolder, StorageFolder destinationFolder)
    {
        if (sourceFolder == null || destinationFolder == null)
        {
            throw new ArgumentNullException("Source or destination folder is null.");
        }

        var destinationSubFolder = await destinationFolder.CreateFolderAsync(sourceFolder.Name, CreationCollisionOption.FailIfExists);
        _pastedPaths.Add(destinationSubFolder.Path);

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

    public static void PasteResource(ResourceKey folderResourceKey)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IPasteResourceCommand>(command => command.FolderResourceKey = folderResourceKey);
    }
}
