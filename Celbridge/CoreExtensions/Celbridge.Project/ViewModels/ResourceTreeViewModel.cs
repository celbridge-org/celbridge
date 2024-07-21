using Celbridge.BaseLibrary.Clipboard;
using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.RootFolder.Children;

    private bool _resourceTreeUpdatePending;

    public ResourceTreeViewModel(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _projectService = workspaceWrapper.WorkspaceService.ProjectService;
        _commandService = commandService;
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

    private void OnClipboardContentChangedMessage(object recipient, ClipboardContentChangedMessage message)
    {
    }

    public void OnLoaded()
    {
        // Listen for messages to determine when to update the resource tree
        _messengerService.Register<RequestResourceTreeUpdateMessage>(this, OnRequestResourceTreeUpdateMessage);
        _messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage);
        _messengerService.Register<ClipboardContentChangedMessage>(this, OnClipboardContentChangedMessage);
    }

    public void OnUnloaded()
    {
        // Listen for messages to determine when to update the resource tree
        _messengerService.Unregister<RequestResourceTreeUpdateMessage>(this);
        _messengerService.Unregister<ExecutedCommandMessage>(this);
        _messengerService.Unregister<ClipboardContentChangedMessage>(this);
    }
}
