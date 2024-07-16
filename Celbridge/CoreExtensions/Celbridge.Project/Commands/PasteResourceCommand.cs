using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Celbridge.Project.Commands;

public class PasteResourceCommand : CommandBase, IPasteResourceCommand
{
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public override string StackName => CommandStackNames.None;

    public ResourceKey FolderResourceKey { get; set; }

    public PasteResourceCommand(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
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

        bool modified = false;
        if (dataPackageView.Contains(StandardDataFormats.StorageItems))
        {
            var storageItems = await dataPackageView.GetStorageItemsAsync();
            try
            {
                foreach (var storageItem in storageItems)
                {
                    if (storageItem is StorageFile storageFile)
                    {
                        await storageFile.CopyAsync(pasteFolder);
                        modified = true;
                    }
                    else if (storageItem is StorageFolder storageFolder)
                    {
                        await CopyStorageFolderAsync(storageFolder, pasteFolder);
                        modified = true;
                    }
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to paste resources. {ex}");
            }
        }

        if (modified)
        {
            resourceRegistry.SetFolderIsExpanded(FolderResourceKey, true);

            var message = new RequestResourceTreeUpdate();
            _messengerService.Send(message);
        }

        return Result.Ok();
    }

    private async Task CopyStorageFolderAsync(StorageFolder sourceFolder, StorageFolder destinationFolder)
    {
        try
        {
            if (sourceFolder == null || destinationFolder == null)
            {
                throw new ArgumentNullException("Source or destination folder is null.");
            }

            var destinationSubFolder = await destinationFolder.CreateFolderAsync(sourceFolder.Name, CreationCollisionOption.OpenIfExists);

            var files = await sourceFolder.GetFilesAsync();
            foreach (var file in files)
            {
                try
                {
                    await file.CopyAsync(destinationSubFolder, file.Name, NameCollisionOption.ReplaceExisting);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error copying file {file.Name}: {ex.Message}");
                }
            }

            var subFolders = await sourceFolder.GetFoldersAsync();
            foreach (var subFolder in subFolders)
            {
                try
                {
                    await CopyStorageFolderAsync(subFolder, destinationSubFolder);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error copying subfolder {subFolder.Name}: {ex.Message}");
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Access denied: {ex.Message}");
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine($"File not found: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    public static void PasteResource(ResourceKey folderResourceKey)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ICopyResourceCommand>(command => command.ResourceKey = folderResourceKey);
    }
}
