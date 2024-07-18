using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Project.Services;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class AddResourceCommand : CommandBase, IAddResourceCommand
{
    public override string UndoStackName => UndoStackNames.Project;

    public ResourceType ResourceType { get; set; }
    public ResourceKey ResourceKey { get; set; }
    public string SourcePath { get; set; } = string.Empty;

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    private string _addedResourcePath = string.Empty;

    public AddResourceCommand(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IProjectDataService projectDataService,
        IUtilityService utilityService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _projectDataService = projectDataService;
        _utilityService = utilityService;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var addResult = await AddResourceAsync();
        //if (addResult.IsFailure)
        //{
        //    var titleString = _stringLocalizer.GetString("ResourceTree_AddFile");
        //    var messageString = _stringLocalizer.GetString("ResourceTree_AddFileFailed", ResourceKey);

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
        //    var messageString = _stringLocalizer.GetString("ResourceTree_UndoAddFileFailed", ResourceKey);

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
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the resource key
        //

        if (ResourceKey.IsEmpty)
        {
            return Result.Fail("Failed to create resource. Resource key is empty");
        }

        if (!ResourceKey.IsValidKey(ResourceKey))
        {
            return Result.Fail($"Failed to create resource. Resource key '{ResourceKey}' is not valid.");
        }

        //
        // Create the resource on disk
        //

        try
        {
            var addedResourcePath = resourceRegistry.GetResourcePath(ResourceKey);

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
        var parentFolderKey = ResourceKey.GetParent();
        if (!parentFolderKey.IsEmpty)
        {
            resourceRegistry.SetFolderIsExpanded(parentFolderKey, true);
        }

        var message = new RequestResourceTreeUpdateMessage();
        _messengerService.Send(message);

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

        var message = new RequestResourceTreeUpdateMessage();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void AddFile(ResourceKey resourceKey, string sourcePath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.ResourceKey = resourceKey;
            command.SourcePath = sourcePath;
        });
    }

    public static void AddFile(ResourceKey resourceKey)
    {
        AddFile(resourceKey, string.Empty);
    }

    public static void AddFolder(ResourceKey resourceKey, string sourcePath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.ResourceKey = resourceKey;
            command.SourcePath = sourcePath;
        });
    }

    public static void AddFolder(ResourceKey resourceKey)
    {
        AddFolder(resourceKey, string.Empty);
    }
}
