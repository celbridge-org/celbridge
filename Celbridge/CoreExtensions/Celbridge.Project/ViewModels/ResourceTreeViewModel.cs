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
    private readonly ILoggingService _loggingService;
    private readonly IProjectDataService _projectDataService;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.Resources;

    private LocalizedString AddFolderText => _stringLocalizer.GetString("InputTextDialog_AddFolder");
    private LocalizedString AddFileText => _stringLocalizer.GetString("InputTextDialog_AddFile");
    private LocalizedString EnterNameText => _stringLocalizer.GetString("InputTextDialog_EnterName");

    public ResourceTreeViewModel(
        ILoggingService loggingService,
        IProjectDataService projectDataService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _loggingService = loggingService;
        _projectDataService = projectDataService;
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

    public void OnAddFolder(FolderResource? folderResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var path = folderResource is null ? string.Empty : resourceRegistry.GetResourcePath(folderResource);

        async Task ShowDialogAsync()
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();

            var showResult = await _dialogService.ShowInputTextDialogAsync(AddFolderText, EnterNameText, invalidCharacters);
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

    public void OnAddFile(FolderResource folderResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var path = folderResource is null ? string.Empty : resourceRegistry.GetResourcePath(folderResource);

        async Task ShowDialogAsync()
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();

            var showResult = await _dialogService.ShowInputTextDialogAsync(AddFileText, EnterNameText, invalidCharacters);
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
}
