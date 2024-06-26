using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;
    private readonly IProjectService _projectService;
    private readonly ICommandService _commandService;

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.Resources;

    public ResourceTreeViewModel(
        ILoggingService loggingService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _loggingService = loggingService;
        _projectService = workspaceWrapper.WorkspaceService.ProjectService;
        _commandService = commandService;
    }

    public void OnExpandedFoldersChanged()
    {
        _commandService.RemoveCommandsOfType<ISaveWorkspaceStateCommand>();
        _commandService.Execute<ISaveWorkspaceStateCommand>(250);
    }
}
