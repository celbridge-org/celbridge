using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Validators;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Project.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.Resources;

    private LocalizedString AddFolderString => _stringLocalizer.GetString("ResourceTree_AddFolder");
    private LocalizedString AddFileString => _stringLocalizer.GetString("ResourceTree_AddFile");
    private LocalizedString EnterNameString => _stringLocalizer.GetString("ResourceTree_EnterName");
    private LocalizedString DeleteString => _stringLocalizer.GetString("ResourceTree_Delete");
    private LocalizedString EnterNewNameString => _stringLocalizer.GetString("ResourceTree_EnterNewName");

    public ResourceTreeViewModel(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _serviceProvider = serviceProvider;
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

    public void AddFolder(IFolderResource? parentFolder)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var path = parentFolder is null ? string.Empty : resourceRegistry.GetResourcePath(parentFolder);

        async Task ShowDialogAsync()
        {
            var defaultText = FindDefaultResourceName("ResourceTree_DefaultFolderName", parentFolder);

            var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
            validator.ParentFolder = parentFolder;

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                AddFolderString, 
                EnterNameString, 
                defaultText,
                ..,
                validator);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                // Execute a command to add the folder resource
                var resourcePath = string.IsNullOrEmpty(path) ? showResult.Value : $"{path}/{showResult.Value}";
                _commandService.Execute<IAddFolderCommand>(command => command.ResourcePath = resourcePath);
            }
        }

        _ = ShowDialogAsync();
    }

    public void AddFile(IFolderResource? parentFolder)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var path = parentFolder is null ? string.Empty : resourceRegistry.GetResourcePath(parentFolder);

        async Task ShowDialogAsync()
        {
            var defaultText = FindDefaultResourceName("ResourceTree_DefaultFileName", parentFolder);

            var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
            validator.ParentFolder = parentFolder;

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                AddFileString, 
                EnterNameString, 
                defaultText,
                .., // Select the entire range
                validator);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                // Execute a command to add the file resource
                var resourcePath = string.IsNullOrEmpty(path) ? showResult.Value : $"{path}/{showResult.Value}";
                _commandService.Execute<IAddFileCommand>(command => command.ResourcePath = resourcePath);
            }
        }

        _ = ShowDialogAsync();
    }

    public void DeleteFolder(FolderResource folderResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourcePath = resourceRegistry.GetResourcePath(folderResource);

        var resourceName = folderResource.Name;
        var confirmDeleteFolderString = _stringLocalizer.GetString("ResourceTree_ConfirmDeleteFolder", resourceName);

        async Task ShowDialogAsync()
        {
            var showResult = await _dialogService.ShowConfirmationDialogAsync(DeleteString, confirmDeleteFolderString);
            if (showResult.IsSuccess)
            {
                var confirmed = showResult.Value;
                if (confirmed)
                {
                    // Execute a command to delete the folder resource
                    _commandService.Execute<IDeleteFolderCommand>(command => command.ResourcePath = resourcePath);
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
        var confirmDeleteFileString = _stringLocalizer.GetString("ResourceTree_ConfirmDeleteFile", resourceName);

        async Task ShowDialogAsync()
        {
            var showResult = await _dialogService.ShowConfirmationDialogAsync(DeleteString, confirmDeleteFileString);
            if (showResult.IsSuccess)
            {
                var confirmed = showResult.Value;
                if (confirmed)
                {
                    // Execute a command to delete the file resource
                    _commandService.Execute<IDeleteFileCommand>(command => command.ResourcePath = resourcePath);
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
            var renameResourceString = _stringLocalizer.GetString("ResourceTree_RenameResource", resourceName);

            var defaultText = resourceName;

            var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
            validator.ParentFolder = folderResource.ParentFolder;

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                renameResourceString, 
                EnterNewNameString, 
                defaultText,
                .., // Select the entire range
                validator);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                var fromResourcePath = resourcePath;
                var toResourcePath = Path.GetDirectoryName(resourcePath);
                toResourcePath = string.IsNullOrEmpty(toResourcePath) ? inputText : toResourcePath + "/" + inputText;

                // Execute a command to move the folder resource to perform the rename
                _commandService.Execute<IMoveFolderCommand>(command =>
                {
                    command.FromResourcePath = fromResourcePath;
                    command.ToResourcePath = toResourcePath;
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
            var renameResourceString = _stringLocalizer.GetString("ResourceTree_RenameResource", resourceName);

            var defaultText = resourceName;
            var selectedRange = new Range(0, Path.GetFileNameWithoutExtension(resourceName).Length); // Don't select extension

            var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
            validator.ParentFolder = fileResource.ParentFolder;

            var showResult = await _dialogService.ShowInputTextDialogAsync(
                renameResourceString, 
                EnterNewNameString, 
                defaultText,
                selectedRange,
                validator);
            if (showResult.IsSuccess)
            {
                var inputText = showResult.Value;

                var fromResourcePath = resourcePath;
                var toResourcePath = Path.GetDirectoryName(resourcePath);
                toResourcePath = string.IsNullOrEmpty(toResourcePath) ? inputText : toResourcePath + "/" + inputText;

                // Execute a command to move the file resource to perform the rename
                _commandService.Execute<IMoveFileCommand>(command =>
                {
                    command.FromResourcePath = fromResourcePath;
                    command.ToResourcePath = toResourcePath;
                });
            }
        }

        _ = ShowDialogAsync();
    }

    // Find a localized default resource name that doesn't clash with an existing resource on disk.
    private string FindDefaultResourceName(string stringKey, IFolderResource? parentFolder)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        string defaultResourceName;
        if (parentFolder is null)
        {
            defaultResourceName = _stringLocalizer.GetString(stringKey, 1).ToString();
        }
        else
        {
            int resourceNumber = 1;
            while (true)
            {
                var parentFolderPath = resourceRegistry.GetPath(parentFolder);
                var candidateName = _stringLocalizer.GetString(stringKey, resourceNumber).ToString();
                var candidatePath = Path.Combine(parentFolderPath, candidateName);
                if (!Directory.Exists(candidatePath) &&
                    !File.Exists(candidatePath))
                {
                    defaultResourceName = candidateName;
                    break;
                }
                resourceNumber++;
            }
        }

        return defaultResourceName;
    }
}
