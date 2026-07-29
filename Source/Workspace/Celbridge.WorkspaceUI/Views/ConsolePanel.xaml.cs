using Celbridge.WorkspaceUI.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.WorkspaceUI.Views;

/// <summary>
/// The bottom-panel banners host: shows the project-notification banners (project error, project
/// change, migration, and project check) for the workspace shell.
/// </summary>
public sealed partial class ConsolePanel : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private string TitleText => _stringLocalizer.GetString("ConsolePanel_Title");

    public ConsolePanelViewModel ViewModel { get; }

    public ConsolePanel()
    {
        this.InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        ViewModel = ServiceLocator.AcquireService<ConsolePanelViewModel>();
    }

    public void Cleanup()
    {
        ViewModel.Cleanup();
    }
}
