using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Project.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly IProjectService _projectService;

    public ObservableCollection<IResource> Children => _projectService.ResourceRegistry.Resources;

    public ResourceTreeViewModel(
        IUserInterfaceService userInterface)
    {
        _projectService = userInterface.WorkspaceService.ProjectService;
    }
}
