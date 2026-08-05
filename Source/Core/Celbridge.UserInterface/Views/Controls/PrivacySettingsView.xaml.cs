using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The Privacy section of the Settings page: clears the cookies, cached credentials, site data and cache
/// shared by every WebView in the application. Composed onto SettingsPage.
/// </summary>
public sealed partial class PrivacySettingsView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private string PrivacySectionString => _stringLocalizer.GetString("Settings_Privacy_SectionHeader");
    private string PrivacyDescriptionString => _stringLocalizer.GetString("Settings_Privacy_Description");
    private string ClearBrowsingDataString => _stringLocalizer.GetString("Settings_Privacy_ClearBrowsingData");
    private string ClearBrowsingDataHintString => _stringLocalizer.GetString("Settings_Privacy_ClearBrowsingDataHint");

    public PrivacySettingsViewModel ViewModel { get; }

    public PrivacySettingsView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<PrivacySettingsViewModel>();

        this.InitializeComponent();
    }
}
