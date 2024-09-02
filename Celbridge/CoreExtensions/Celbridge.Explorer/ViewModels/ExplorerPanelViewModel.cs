using Celbridge.Commands;
using Celbridge.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Explorer.ViewModels;

public partial class ExplorerPanelViewModel : ObservableObject
{
    private readonly ICommandService _commandService;

    [ObservableProperty]
    private string _titleText = string.Empty;

    public ExplorerPanelViewModel(
        IProjectService projectService,
        ICommandService commandService)
    {
        _commandService = commandService;

        // The project data is guaranteed to have been loaded at this point, so it's safe to just
        // acquire a reference via the ProjectService.
        var project = projectService.LoadedProject!;

        TitleText = project.ProjectName;
    }

    public ICommand RefreshResourceTreeCommand => new RelayCommand(RefreshResourceTreeCommand_ExecuteAsync);
    private void RefreshResourceTreeCommand_ExecuteAsync()
    {
        _commandService.Execute<IUpdateResourcesCommand>();
    }
}
