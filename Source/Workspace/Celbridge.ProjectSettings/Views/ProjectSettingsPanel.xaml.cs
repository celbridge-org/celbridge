using Celbridge.ProjectSettings.ViewModels;
using Celbridge.UserInterface;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.Views;

public sealed partial class ProjectSettingsPanel : UserControl, IProjectSettingsPanel
{
    private readonly IStringLocalizer _stringLocalizer;

    public ProjectSettingsPanelViewModel ViewModel { get; }

    public string InformationHeader => _stringLocalizer.GetString("ProjectSettings_InformationHeader");
    public string PackagesHeader => _stringLocalizer.GetString("ProjectSettings_PackagesHeader");
    public string FileEditorsHeader => _stringLocalizer.GetString("ProjectSettings_FileEditorsHeader");
    public string ApplyAndReloadText => _stringLocalizer.GetString("ProjectSettings_ApplyAndReload");

    public ProjectSettingsPanel()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        ViewModel = ServiceLocator.AcquireService<ProjectSettingsPanelViewModel>();

        InitializeComponent();

        Loaded += (sender, e) => ViewModel.Refresh();
    }

    public void FocusPanel()
    {
        Focus(FocusState.Programmatic);
    }

    public void Refresh()
    {
        ViewModel.Refresh();
    }

    private void PanelHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        FocusPanel();
    }

    private void NavTab_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element
            && element.Tag is string tag
            && int.TryParse(tag, out var index))
        {
            ViewModel.SelectedSectionIndex = index;
        }
    }
}
