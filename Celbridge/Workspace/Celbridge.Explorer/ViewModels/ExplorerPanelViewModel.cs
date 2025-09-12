using Celbridge.Commands;
using Celbridge.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.ViewModels;

public partial class ExplorerPanelViewModel : ObservableObject
{
    private readonly ICommandService _commandService;
    private IStringLocalizer _stringLocalizer;

    [ObservableProperty]
    private string _titleText = string.Empty;

    public ExplorerPanelViewModel(
        IProjectService projectService,
        ICommandService commandService)
    {
        _commandService = commandService;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        // The project data is guaranteed to have been loaded at this point, so it's safe to just
        // acquire a reference via the ProjectService.
        var project = projectService.CurrentProject!;

        // On Windows, the project title will be shown in the custom title bar. On other platforms, currently, it will not, so continue to show it on the explorer top banner.
        //  Note : This behaviour is likely to change once we visit how the title bar may work on Mac and Linux.
#if WINDOWS
        TitleText = _stringLocalizer.GetString("ExplorerPanel_Title");
#else
        TitleText = project.ProjectName;
#endif
    }

    public ICommand RefreshResourceTreeCommand => new RelayCommand(RefreshResourceTreeCommand_ExecuteAsync);
    private void RefreshResourceTreeCommand_ExecuteAsync()
    {
        _commandService.Execute<IUpdateResourcesCommand>();
    }
}
