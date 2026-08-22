using Celbridge.Dialog;
using Celbridge.UserInterface.ViewModels.Dialogs;
using Celbridge.UserInterface.Views.Controls;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The application settings dialog. A rail of categories over a content pane, each category a
/// self-contained control shown one at a time.
/// </summary>
public sealed partial class SettingsDialog : ContentDialog, ISettingsDialog
{
    private readonly IStringLocalizer _stringLocalizer;

    private string TitleString => _stringLocalizer.GetString("Settings_DialogTitle");

    public SettingsDialogViewModel ViewModel { get; }

    public SettingsDialog()
    {
        // The labels are bound one-time, so the localizer has to be in place before the XAML loads.
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<SettingsDialogViewModel>();
        ViewModel.InitializeSections(BuildSections());

        this.InitializeComponent();

        // The close button carries no text, so the label it reports comes from here.
        var closeText = _stringLocalizer.GetString("DialogButton_Close");
        ToolTipService.SetToolTip(CloseButton, closeText);
        AutomationProperties.SetName(CloseButton, closeText);

        this.EnableThemeSync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    // The categories in rail order. The keys are persisted, so changing one drops the category a
    // returning user had open.
    private List<SettingsSection> BuildSections()
    {
        var sections = new List<SettingsSection>
        {
            new(
                "Appearance",
                "bs-palette",
                _stringLocalizer.GetString("Settings_Appearance_SectionHeader"),
                _stringLocalizer.GetString("Settings_Appearance_Description"),
                new AppearanceSettingsView()),
            new(
                "Workshop",
                "bs-shop",
                _stringLocalizer.GetString("Settings_Workshop_SectionHeader"),
                _stringLocalizer.GetString("Settings_Workshop_Description"),
                new WorkshopSettingsView()),
            new(
                "WebView",
                "bs-globe",
                _stringLocalizer.GetString("Settings_WebView_SectionHeader"),
                _stringLocalizer.GetString("Settings_WebView_Description"),
                new WebViewSettingsView()),
        };

        return sections;
    }

    public async Task ShowDialogAsync()
    {
        await ShowAsync();
    }
}
