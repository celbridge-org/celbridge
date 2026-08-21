using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The Privacy section of the settings dialog.
/// </summary>
public sealed partial class PrivacySettingsView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private string PrivacySectionString => _stringLocalizer.GetString("Settings_Privacy_SectionHeader");
    private string PrivacyDescriptionString => _stringLocalizer.GetString("Settings_Privacy_Description");
    private string ClearBrowsingDataString => _stringLocalizer.GetString("Settings_Privacy_ClearBrowsingData");
    private string ClearBrowsingDataHintString => _stringLocalizer.GetString("Settings_Privacy_ClearBrowsingDataHint");
    private string ClearConfirmMessageString => _stringLocalizer.GetString("Settings_Privacy_ClearDialogMessage");
    private string ClearConfirmButtonString => _stringLocalizer.GetString("Settings_Privacy_ClearConfirmButton");
    private string CancelString => _stringLocalizer.GetString("DialogButton_Cancel");

    public PrivacySettingsViewModel ViewModel { get; }

    public PrivacySettingsView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<PrivacySettingsViewModel>();

        this.InitializeComponent();
    }
}
