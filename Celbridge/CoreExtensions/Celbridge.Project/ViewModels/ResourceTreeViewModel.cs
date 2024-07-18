using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.RootFolder.Children;

    private bool _resourceTreeUpdatePending;

    public ResourceTreeViewModel(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _projectService = workspaceWrapper.WorkspaceService.ProjectService;
        _commandService = commandService;

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
