using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Project.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;
    private readonly IProjectService _projectService;

    private ObservableCollection<Resource> _children = new();
    public ObservableCollection<Resource> Children
    {
        get
        {
            return _children;
        }
        set
        {
            SetProperty(ref _children, value);
        }
    }

    public ResourceTreeViewModel(
        ILoggingService loggingService,
        IUserInterfaceService userInterface)
    {
        _loggingService = loggingService;
        _projectService = userInterface.WorkspaceService.ProjectService;

        ScanProjectFolder();
    }

    private void ScanProjectFolder()
    {
        _children.Clear();

        var projectFolder = _projectService.LoadedProjectData.ProjectFolder;
        _loggingService.Info($"Scanning {projectFolder}");

        var scanResult = ResourceScanner.ScanFolderResources(projectFolder);
        if (scanResult.IsFailure)
        {
            _loggingService.Error(scanResult.Error);
            return;
        }

        _children = scanResult.Value;
    }
}
