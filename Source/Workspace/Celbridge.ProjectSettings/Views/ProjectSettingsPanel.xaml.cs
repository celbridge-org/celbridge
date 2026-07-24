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
    public string PagesHeader => _stringLocalizer.GetString("ProjectSettings_PagesHeader");
    public string ReloadProjectText => _stringLocalizer.GetString("ProjectSettings_ReloadProject");
    public string ReloadCaptionText => _stringLocalizer.GetString("ProjectSettings_ReloadCaption");

    public ProjectSettingsPanel()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        ViewModel = ServiceLocator.AcquireService<ProjectSettingsPanelViewModel>();

        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyReloadButtonEmphasis();

        Loaded += (sender, e) => ViewModel.Refresh();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSettingsPanelViewModel.HasPendingChanges))
        {
            ApplyReloadButtonEmphasis();
        }
    }

    // A Style cannot be bound in XAML without a converter, so the swap happens here.
    private void ApplyReloadButtonEmphasis()
    {
        var styleKey = ViewModel.HasPendingChanges ? "AccentButtonStyle" : "DefaultButtonStyle";
        if (Application.Current.Resources.TryGetValue(styleKey, out var style)
            && style is Style buttonStyle)
        {
            ReloadProjectButton.Style = buttonStyle;
        }
    }

    public void FocusPanel()
    {
        Focus(FocusState.Programmatic);
    }

    // Focuses the panel in response to a click on its chrome (the header or a section tab) so the workspace
    // reports Project Settings as the focused panel and lights its rail button. Uses Pointer focus rather
    // than the Programmatic focus of FocusPanel, which the focus tracker ignores because it is reserved for
    // restoration.
    private void FocusPanelFromClick()
    {
        Focus(FocusState.Pointer);
    }

    public void Refresh()
    {
        ViewModel.Refresh();
    }

    private void PanelHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        FocusPanelFromClick();
    }

    private void NavTab_Tapped(object sender, TappedRoutedEventArgs e)
    {
        FocusPanelFromClick();

        if (sender is FrameworkElement element
            && element.Tag is string tag
            && int.TryParse(tag, out var index))
        {
            ViewModel.SelectedSectionIndex = index;
        }
    }
}
