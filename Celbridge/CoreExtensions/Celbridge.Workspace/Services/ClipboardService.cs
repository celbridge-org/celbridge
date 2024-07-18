using Celbridge.BaseLibrary.Clipboard;
using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Celbridge.Workspace.Services;

public class ClipboardService : IClipboardService
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
    }

    public ClipboardContentType GetClipboardContentType()
    {
        var dataPackageView = Clipboard.GetContent();
        if (dataPackageView.Contains(StandardDataFormats.StorageItems))
        {
            return ClipboardContentType.Resource;
        }
        else if (dataPackageView.Contains(StandardDataFormats.Text))
        {
            return ClipboardContentType.Text;
        }

        return ClipboardContentType.None;
    }

    public async Task<Result> PasteResources(ResourceKey folderResource)
    {
        if (GetClipboardContentType() != ClipboardContentType.Resource)
        {
            return Result.Fail("Clipboard does not contain a resource to paste");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(folderResource);
        if (getResult.IsFailure)
        {
            return getResult;
        }

        var resource = getResult.Value;
        if (resource is not IFolderResource)
        {
            return Result.Fail($"'{folderResource}' is not a folder resource.");
        }

        var pasteFolderPath = resourceRegistry.GetResourcePath(resource);
        if (!Directory.Exists(pasteFolderPath))
        {
            return Result.Fail($"The path '{pasteFolderPath}' does not exist.");
        }

        var dataPackageView = Clipboard.GetContent();

        // Determine if the operation is a move or a copy
        var operation = dataPackageView.RequestedOperation == DataPackageOperation.Move ? CopyResourceOperation.Move : CopyResourceOperation.Copy;

        bool modified = false;
        if (dataPackageView.Contains(StandardDataFormats.StorageItems))
        {
            var storageItems = await dataPackageView.GetStorageItemsAsync();
            try
            {
                foreach (var storageItem in storageItems)
                {
                    // If this item is in the project folder, we can use the CopyResource command to copy/move it
                    var storageItemPath = storageItem.Path;

                    if (PathContainsSubPath(pasteFolderPath, storageItemPath))
                    {
                        return Result.Fail("Cannot paste a resource into a subfolder of itself.");
                    }

                    var getKeyResult = resourceRegistry.GetResourceKey(storageItemPath);
                    if (getKeyResult.IsSuccess)
                    {
                        // This is a resource in the project folder, so we can copy/move it using the CopyResource command.
                        var sourceResource = getKeyResult.Value;
                        _commandService.Execute<ICopyResourceCommand>(command =>
                        {
                            command.SourceResource = sourceResource;
                            command.DestResource = folderResource;
                            command.Operation = operation;
                        });

                        modified = true;
                    }
                    else
                    {
                        // This is a resource from outside the project folder, so we need to add it via the AddResource command
                        if (storageItem is StorageFile file)
                        {
                            _commandService.Execute<IAddResourceCommand>(command =>
                            {
                                command.ResourceType = ResourceType.File;
                                command.Resource = folderResource.Combine(file.Name);
                                command.SourcePath = file.Path;
                            });

                            modified = true;
                        }
                        else if (storageItem is StorageFolder folder)
                        {
                            _commandService.Execute<IAddResourceCommand>(command =>
                            {
                                command.ResourceType = ResourceType.Folder;
                                command.Resource = folderResource.Combine(folder.Name);
                                command.SourcePath = folder.Path;
                            });

                            modified = true;
                        }
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

        resourceRegistry.SetFolderIsExpanded(folderResource, true);

        var message = new RequestResourceTreeUpdateMessage();
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
}
