using Celbridge.Commands;
using Celbridge.Platform;
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
        ICommandService commandService,
        IPlatformInfo platformInfo)
    {
        _commandService = commandService;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        // The project data is guaranteed to have been loaded at this point, so it's safe to just
        // acquire a reference via the ProjectService.
        var project = projectService.CurrentProject!;

        // When the host chrome (the custom title bar) shows the project name, the banner shows a generic
        // title instead of duplicating it; otherwise the banner carries the project name.
        if (platformInfo.HostShowsProjectTitleInChrome)
        {
            TitleText = _stringLocalizer.GetString("ExplorerPanel_Title");
        }
        else
        {
            TitleText = project.ProjectName;
        }
    }
}
