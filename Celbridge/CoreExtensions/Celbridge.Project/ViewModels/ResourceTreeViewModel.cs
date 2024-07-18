using Celbridge.BaseLibrary.Clipboard;
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
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly IClipboardService _clipboardService;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.RootFolder.Children;

    private LocalizedString EnterNameString => _stringLocalizer.GetString("ResourceTree_EnterName");
    private LocalizedString DeleteString => _stringLocalizer.GetString("ResourceTree_Delete");
    private LocalizedString EnterNewNameString => _stringLocalizer.GetString("ResourceTree_EnterNewName");

    private bool _resourceTreeUpdatePending;

    public ResourceTreeViewModel(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _projectService = workspaceWrapper.WorkspaceService.ProjectService;
        _clipboardService = workspaceWrapper.WorkspaceService.ClipboardService;
        _commandService = commandService;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;

        // Listen for messages to determine when to update the resource tree
        messengerService.Register<RequestResourceTreeUpdateMessage>(this, OnRequestResourceTreeUpdateMessage);
        messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage); 
    }

    private void OnRequestResourceTreeUpdateMessage(object recipient, RequestResourceTreeUpdateMessage message)
    {
        // Set a flag to update the resource tree
        _resourceTreeUpdatePending = true;
    }

    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        if (_resourceTreeUpdatePending)
        {
            // Don't update if there are any pending resource tree commands
            if (!_commandService.ContainsCommandsOfType<IAddResourceCommand>() &&
                !_commandService.ContainsCommandsOfType<IDeleteResourceCommand>() &&
                !_commandService.ContainsCommandsOfType<ICopyResourceCommand>() &&
                !_commandService.ContainsCommandsOfType<IUpdateResourceTreeCommand>())
            {
                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
                _resourceTreeUpdatePending = false;
            }
        }
    }
}
