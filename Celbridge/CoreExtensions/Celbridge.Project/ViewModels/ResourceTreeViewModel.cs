using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Project.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.Resources;

    private LocalizedString AddFolderText => _stringLocalizer.GetString("ResourceTree_AddFolder");
    private LocalizedString AddFileText => _stringLocalizer.GetString("ResourceTree_AddFile");
    private LocalizedString EnterNameText => _stringLocalizer.GetString("ResourceTree_EnterName");
    private LocalizedString DeleteText => _stringLocalizer.GetString("ResourceTree_Delete");
    private LocalizedString EnterNewNameText => _stringLocalizer.GetString("ResourceTree_EnterNewName");

    public ResourceTreeViewModel(
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _projectService = workspaceWrapper.WorkspaceService.ProjectService;
        _commandService = commandService;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
    }

    public void OnExpandedFoldersChanged()
    {
        _commandService.RemoveCommandsOfType<ISaveWorkspaceStateCommand>();
        _commandService.Execute<ISaveWorkspaceStateCommand>(250);
    }

    public void AddFolder(FolderResource? folderResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var path = folderResource is null ? string.Empty : resourceRegistry.GetResourcePath(folderResource);

        async Task ShowDialogAsync()
        {
            var defaultText = FindDefaultResourceName("ResourceTree_DefaultFolderName", folderResource);
            var invalidCharacters = Path.GetInvalidFileNameChars();

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                AddFolderText, 
                EnterNameText, 
                defaultText,
                ..,
                invalidCharacters);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                // Execute a command to add the folder resource
                var resourcePath = string.IsNullOrEmpty(path) ? showResult.Value : $"{path}/{showResult.Value}";
                _commandService.Execute<IAddFolderCommand>(command => command.FolderPath = resourcePath);
            }
        }

        _ = ShowDialogAsync();
    }

    public void AddFile(FolderResource? folderResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var path = folderResource is null ? string.Empty : resourceRegistry.GetResourcePath(folderResource);

        async Task ShowDialogAsync()
        {
            var defaultText = FindDefaultResourceName("ResourceTree_DefaultFileName", folderResource);
            var invalidCharacters = Path.GetInvalidFileNameChars();

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                AddFileText, 
                EnterNameText, 
                defaultText,
                .., // Select the entire range
                invalidCharacters);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                // Execute a command to add the file resource
                var resourcePath = string.IsNullOrEmpty(path) ? showResult.Value : $"{path}/{showResult.Value}";
                _commandService.Execute<IAddFileCommand>(command => command.FilePath = resourcePath);
            }
        }

        _ = ShowDialogAsync();
    }

    public void DeleteFolder(FolderResource folderResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourcePath = resourceRegistry.GetResourcePath(folderResource);

        var resourceName = folderResource.Name;
        var confirmDeleteText = _stringLocalizer.GetString("ResourceTree_ConfirmDeleteFolder", resourceName);

        async Task ShowDialogAsync()
        {
            var showResult = await _dialogService.ShowConfirmationDialogAsync(DeleteText, confirmDeleteText);
            if (showResult.IsSuccess)
            {
                var confirmed = showResult.Value;
                if (confirmed)
                {
                    // Execute a command to delete the folder resource
                    _commandService.Execute<IDeleteFolderCommand>(command => command.FolderPath = resourcePath);
                }
            }
        }

        _ = ShowDialogAsync();
    }

    public void DeleteFile(FileResource fileResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourcePath = resourceRegistry.GetResourcePath(fileResource);

        var resourceName = fileResource.Name;
        var confirmDeleteText = _stringLocalizer.GetString("ResourceTree_ConfirmDeleteFile", resourceName);

        async Task ShowDialogAsync()
        {
            var showResult = await _dialogService.ShowConfirmationDialogAsync(DeleteText, confirmDeleteText);
            if (showResult.IsSuccess)
            {
                var confirmed = showResult.Value;
                if (confirmed)
                {
                    // Execute a command to delete the file resource
                    _commandService.Execute<IDeleteFileCommand>(command => command.FilePath = resourcePath);
                }
            }
        }

        _ = ShowDialogAsync();
    }

    public void RenameFolder(FolderResource folderResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourcePath = resourceRegistry.GetResourcePath(folderResource);

        var resourceName = folderResource.Name;

        async Task ShowDialogAsync()
        {
            var renameResourceText = _stringLocalizer.GetString("ResourceTree_RenameResource", resourceName);

            var defaultText = resourceName;
            var invalidCharacters = Path.GetInvalidFileNameChars();

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                renameResourceText, 
                EnterNewNameText, 
                defaultText,
                .., // Select the entire range
                invalidCharacters);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                var fromPath = resourcePath;
                var toPath = Path.GetDirectoryName(resourcePath);
                toPath = string.IsNullOrEmpty(toPath) ? inputText : toPath + "/" + inputText;

                // Execute a command to rename the folder resource
                _commandService.Execute<IMoveFolderCommand>(command =>
                {
                    command.FromFolderPath = resourcePath;
                    command.ToFolderPath = toPath;
                });
            }
        }

        _ = ShowDialogAsync();
    }

    public void RenameFile(FileResource fileResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourcePath = resourceRegistry.GetResourcePath(fileResource);

        var resourceName = fileResource.Name;

        async Task ShowDialogAsync()
        {
            var renameResourceText = _stringLocalizer.GetString("ResourceTree_RenameResource", resourceName);

            var defaultText = resourceName;
            var selectedRange = new Range(0, Path.GetFileNameWithoutExtension(resourceName).Length); // Don't select extension
            var invalidCharacters = Path.GetInvalidFileNameChars();

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                renameResourceText, 
                EnterNewNameText, 
                defaultText,
                selectedRange,
                invalidCharacters);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                var fromPath = resourcePath;
                var toPath = Path.GetDirectoryName(resourcePath);
                toPath = string.IsNullOrEmpty(toPath) ? inputText : toPath + "/" + inputText;

                // Execute a command to rename the folder resource
                _commandService.Execute<IMoveFileCommand>(command =>
                {
                    command.FromFilePath = resourcePath;
                    command.ToFilePath = toPath;
                });
            }
        }

        _ = ShowDialogAsync();
    }

    private string FindDefaultResourceName(string stringKey, FolderResource? parentFolder)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        // Find a default name that doesn't clash with an existing resource on disk
        string defaultName;
        if (parentFolder is null)
        {
            defaultName = _stringLocalizer.GetString(stringKey, 1).ToString();
        }
        else
        {
            int resourceNumber = 1;
            while (true)
            {
                var parentDir = resourceRegistry.GetAbsolutePath(parentFolder);
                var checkName = _stringLocalizer.GetString(stringKey, resourceNumber).ToString();
                var checkPath = Path.Combine(parentDir, checkName);
                if (!Directory.Exists(checkPath) &&
                    !File.Exists(checkPath))
                {
                    defaultName = checkName;
                    break;
                }
                resourceNumber++;
            }
        }

        return defaultName;
    }
}
