using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Project.ViewModels;

public partial class ProjectPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;

    [ObservableProperty]
    private string _titleText = string.Empty;

    public ProjectPanelViewModel(
        IMessengerService messengerService,
        IProjectDataService projectDataService,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _commandService = commandService;

        // The project data is guaranteed to have been loaded at this point, so it's safe to just
        // acquire a reference via the ProjectDataService.
        var projectData = projectDataService.LoadedProjectData!;

        TitleText = projectData.ProjectName;
    }

    public ICommand RefreshResourceTreeCommand => new RelayCommand(RefreshResourceTreeCommand_ExecuteAsync);
    private void RefreshResourceTreeCommand_ExecuteAsync()
    {
        _commandService.Execute<IUpdateResourceTreeCommand>();
    }
}
