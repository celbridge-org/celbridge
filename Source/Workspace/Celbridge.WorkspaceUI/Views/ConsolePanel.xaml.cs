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

    private string ReloadProjectText => _stringLocalizer.GetString("ConsolePanel_ReloadProjectButton");

    public ConsolePanelViewModel ViewModel { get; }

    public ConsolePanel()
    {
        // Acquire the localizer and view model before InitializeComponent so the x:Bind bindings
        // (TitleText, ReloadProjectText, ViewModel.*) have their sources ready when they first evaluate.
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<ConsolePanelViewModel>();

        this.InitializeComponent();
    }

    public void Cleanup()
    {
        ViewModel.Cleanup();
    }
}
