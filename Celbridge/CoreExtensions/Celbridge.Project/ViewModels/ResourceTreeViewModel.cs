using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Commands.Workspace;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ILoggingService _loggingService;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.Resources;

    public ResourceTreeViewModel(
        IMessengerService messengerService,
        ILoggingService loggingService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
        _projectService = workspaceWrapper.WorkspaceService.ProjectService;
        _commandService = commandService;

        _messengerService.Register<RequestProjectRefreshMessage>(this, OnRefreshProjectRequested);
    }

    public void ResourceTreeView_Unloaded()
    {
        _messengerService.Unregister<RequestProjectRefreshMessage>(this);
    }

    private void OnRefreshProjectRequested(object recipient, RequestProjectRefreshMessage message)
    {
        var updateResult = _projectService.ResourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            _loggingService.Error(updateResult.Error);
        }
    }

    public void OnExpandedFoldersChanged()
    {
        _loggingService.Info("Collection changed");



        _commandService.RemoveCommandsOfType<ISaveWorkspaceStateCommand>();
        _commandService.Execute<ISaveWorkspaceStateCommand>(250);
    }
}
