using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Project.Models;
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

    public void OnAddFolder(FolderResource folderResource)
    {
        // Todo: This could probably be a static util instead?
        var resourceRegistry = _projectService.ResourceRegistry;
        var path = resourceRegistry.GetResourcePath(folderResource);

        _loggingService.Info($"Add Folder: {path}");
    }
}
