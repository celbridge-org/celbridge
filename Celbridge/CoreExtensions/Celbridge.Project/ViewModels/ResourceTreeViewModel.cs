using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Project.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;
    private readonly IProjectService _projectService;

    private IResourceRegistry _resourceRegistry;

    public ObservableCollection<IResource> Children => _resourceRegistry.Resources;

    public ResourceTreeViewModel(
        ILoggingService loggingService,
        IUserInterfaceService userInterface)
    {
        _loggingService = loggingService;
        _projectService = userInterface.WorkspaceService.ProjectService;

        var projectFolder = _projectService.LoadedProjectData.ProjectFolder;
        _loggingService.Info($"Scanning {projectFolder}");

        _resourceRegistry = new ResourceRegistry(projectFolder);

        // Todo: Refresh this via a method on the ProjectService instead of here
        var scanResult = _resourceRegistry.ScanResources();
        if (scanResult.IsFailure)
        {
            _loggingService.Error(scanResult.Error);
        }
    }
}
