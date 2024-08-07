using Celbridge.Commands;
using Celbridge.Workspace;
using Celbridge.Resources.Services;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Resources.Commands;

public class AddResourceCommand : CommandBase, IAddResourceCommand
{
    public override string UndoStackName => UndoStackNames.Project;
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceType ResourceType { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public ResourceKey DestResource { get; set; }

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;

    private string _addedResourcePath = string.Empty;

    public AddResourceCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectDataService projectDataService)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectDataService = projectDataService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var addResult = await AddResourceAsync();
        //if (addResult.IsFailure)
        //{
        //    var titleString = _stringLocalizer.GetString("ResourceTree_AddFile");
        //    var messageString = _stringLocalizer.GetString("ResourceTree_AddFileFailed", Resource);

        //    // Show alert
        //    await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        //}

        return addResult;
    }

    public override async Task<Result> UndoAsync()
    {
        var undoResult = await UndoAddResourceAsync();
        //if (undoResult.IsFailure)
        //{
        //    var titleString = _stringLocalizer.GetString("ResourceTree_AddFile");
        //    var messageString = _stringLocalizer.GetString("ResourceTree_UndoAddFileFailed", Resource);

        //    // Show alert
        //    await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        //}

        return undoResult;
    }

    private async Task<Result> AddResourceAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to add resource because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the resource key
        //

        if (DestResource.IsEmpty)
        {
            return Result.Fail("Failed to create resource. Resource key is empty");
        }

        if (!ResourceKey.IsValidKey(DestResource))
        {
            return Result.Fail($"Failed to create resource. Resource key '{DestResource}' is not valid.");
        }

        //
        // Create the resource on disk
        //

        try
        {
            var addedResourcePath = resourceRegistry.GetResourcePath(DestResource);

            // Fail if the parent folder for the new resource does not exist.
            // We could attempt to create any missing parent folders, but it would make the undo logic trickier.
            var parentFolderPath = Path.GetDirectoryName(addedResourcePath);
            if (!Directory.Exists(parentFolderPath))
            {
                return Result.Fail($"Failed to create resource. Parent folder does not exist: '{parentFolderPath}'");
            }

            // It's important to fail if the resource already exists, because undoing this command
            // deletes the resource, which could lead to unexpected data loss.
            if (ResourceType == ResourceType.File)
            {
                if (File.Exists(addedResourcePath))
                {
                    return Result.Fail($"A file already exists at '{addedResourcePath}'.");
                }

                if (string.IsNullOrEmpty(SourcePath))
                {
                    File.WriteAllText(addedResourcePath, string.Empty);
                }
                else
                {
                    if (File.Exists(SourcePath))
                    {
                        File.Copy(SourcePath, addedResourcePath);
                    }
                    else
                    {
                        return Result.Fail($"Failed to create resource. Source file '{SourcePath}' does not exist.");
                    }
                }
            }
            else if (ResourceType == ResourceType.Folder)
            {
                if (Directory.Exists(addedResourcePath))
                {
                    return Result.Fail($"A folder already exists at '{addedResourcePath}'.");
                }

                if (string.IsNullOrEmpty(SourcePath))
                {
                    Directory.CreateDirectory(addedResourcePath);
                }
                else
                {
                    if (Directory.Exists(SourcePath))
                    {
                        ResourceUtils.CopyFolder(SourcePath, addedResourcePath);
                    }
                    else
                    {
                        return Result.Fail($"Failed to create resource. Source folder '{SourcePath}' does not exist.");
                    }
                }
            }

            // Note the path of the added resource for undoing
            _addedResourcePath = addedResourcePath;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create resource. {ex.Message}");
        }

        //
        // Expand the folder containing the newly created resource
        //
        var parentFolderKey = DestResource.GetParent();
        if (!parentFolderKey.IsEmpty)
        {
            resourceRegistry.SetFolderIsExpanded(parentFolderKey, true);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    private async Task<Result> UndoAddResourceAsync()
    {
        //
        // Delete the previously added resource
        //

        try
        {
            // Clear the cached resource path to clean up
            var addedResourcePath = _addedResourcePath;
            _addedResourcePath = string.Empty;

            if (ResourceType == ResourceType.File &&
                File.Exists(addedResourcePath))
            {
                File.Delete(addedResourcePath);
            }
            else if (ResourceType == ResourceType.Folder &&
                Directory.Exists(addedResourcePath))
            {
                Directory.Delete(addedResourcePath, true);
            }

        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo add resource. {ex.Message}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void AddFile(string sourcePath, ResourceKey destResource)
    {
        var workspaceWrapper = ServiceLocator.ServiceProvider.GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            throw new InvalidOperationException("Failed to add resource because workspace is not loaded");
        }

        // If the destination resource is a existing folder, resolve the destination resource to a file in
        // that folder with the same name as the source file.
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;
        var resolvedDestResource = resourceRegistry.ResolveSourcePathDestinationResource(sourcePath, destResource);

        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.SourcePath = sourcePath;
            command.DestResource = resolvedDestResource;
        });
    }

    public static void AddFile(ResourceKey destResource)
    {
        AddFile(new ResourceKey(), destResource);
    }

    public static void AddFolder(string sourcePath, ResourceKey destResource)
    {
        var workspaceWrapper = ServiceLocator.ServiceProvider.GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            throw new InvalidOperationException("Failed to add resource because workspace is not loaded");
        }

        // If the destination resource is a existing folder, resolve the destination resource to a folder in
        // that folder with the same name as the source folder.
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;
        var resolvedDestResource = resourceRegistry.ResolveSourcePathDestinationResource(sourcePath, destResource);

        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.SourcePath = sourcePath;
            command.DestResource = resolvedDestResource;
        });
    }

    public static void AddFolder(ResourceKey destResource)
    {
        AddFolder(new ResourceKey(), destResource);
    }
}
