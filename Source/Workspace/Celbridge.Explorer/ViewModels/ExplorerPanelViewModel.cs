using Celbridge.Commands;
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
        ICommandService commandService)
    {
        _commandService = commandService;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        // The open project's name is shown in the page navigation toolbar, so the panel header carries the
        // generic utility title to match the other workspace panels.
        TitleText = _stringLocalizer.GetString("ExplorerPanel_Title");
    }
}
