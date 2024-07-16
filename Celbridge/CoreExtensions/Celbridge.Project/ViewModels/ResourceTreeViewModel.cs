using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
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

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.RootFolder.Children;

    private LocalizedString AddFolderString => _stringLocalizer.GetString("ResourceTree_AddFolder");
    private LocalizedString AddFileString => _stringLocalizer.GetString("ResourceTree_AddFile");
    private LocalizedString EnterNameString => _stringLocalizer.GetString("ResourceTree_EnterName");
    private LocalizedString DeleteString => _stringLocalizer.GetString("ResourceTree_Delete");
    private LocalizedString EnterNewNameString => _stringLocalizer.GetString("ResourceTree_EnterNewName");

    private bool _resourceTreeUpdatePending;

    public ResourceTreeViewModel(
        IServiceProvider serviceProvider,
        ILoggingService loggingService,
        IMessengerService messengerService,
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

        // Listen for messages to determine when to update the resource tree
        messengerService.Register<RequestResourceTreeUpdate>(this, OnRequestResourceTreeUpdateMessage);
        messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage); 
    }

    private void OnRequestResourceTreeUpdateMessage(object recipient, RequestResourceTreeUpdate message)
    {
        // Set a flag to update the resource tree
        _resourceTreeUpdatePending = true;
    }

    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        if (_resourceTreeUpdatePending)
        {
            // Don't update if there are any pending resource tree commands
            if (!_commandService.ContainsCommandsOfType<IAddFileCommand>() &&
                !_commandService.ContainsCommandsOfType<IAddFolderCommand>() &&
                !_commandService.ContainsCommandsOfType<IDeleteFileCommand>() &&
                !_commandService.ContainsCommandsOfType<IDeleteFolderCommand>() &&
                !_commandService.ContainsCommandsOfType<IMoveFileCommand>() &&
                !_commandService.ContainsCommandsOfType<IMoveFolderCommand>() &&
                !_commandService.ContainsCommandsOfType<IUpdateResourceTreeCommand>())
            {
                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
                _resourceTreeUpdatePending = false;
            }
        }
    }
}
