using Celbridge.UserInterface.ViewModels.Pages;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The Settings Page for configuring application preferences. Sections are
/// composed from self-contained controls (e.g. WorkshopSettingsView); the
/// page itself only hosts the application-wide options such as the theme.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly IStringLocalizer _stringLocalizer;

    private string TitleString => _stringLocalizer.GetString("Settings_Page_Title");
    private string ApplicationThemeString => _stringLocalizer.GetString("Settings_Application_Theme");

    public SettingsPageViewModel ViewModel { get; }

    public SettingsPage()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<SettingsPageViewModel>();

        this.InitializeComponent();
    }
}
