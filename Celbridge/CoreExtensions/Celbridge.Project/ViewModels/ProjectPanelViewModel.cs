using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Project.ViewModels;

public partial class ProjectPanelViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly ILoggingService _loggingService;

    [ObservableProperty]
    private string _titleText = string.Empty;

    public ProjectPanelViewModel(
        ILoggingService loggingService,
        IUserInterfaceService userInterfaceService)
    {
        _loggingService = loggingService;

        _projectService = userInterfaceService.WorkspaceService.ProjectService;

        // The project data is guaranteed to have been loaded at this point, so it's safe to just
        // acquire a reference via the ProjectService.
        var projectData = _projectService.LoadedProjectData;

        TitleText = projectData.ProjectName;
    }

    public ICommand RefreshProjectCommand => new RelayCommand(RefreshProjectCommand_ExecuteAsync);
    private void RefreshProjectCommand_ExecuteAsync()
    {
        var updateResult = _projectService.ResourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            _loggingService.Error(updateResult.Error);
        }
    }
}
