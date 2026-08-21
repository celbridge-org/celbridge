using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The Web View section of the settings dialog, covering how hosted web content behaves.
/// </summary>
public sealed partial class WebViewSettingsView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private string ClearBrowsingDataString => _stringLocalizer.GetString("Settings_WebView_ClearBrowsingData");
    private string ClearBrowsingDataHintString => _stringLocalizer.GetString("Settings_WebView_ClearBrowsingDataHint");
    private string ClearConfirmMessageString => _stringLocalizer.GetString("Settings_WebView_ClearDialogMessage");
    private string ClearConfirmButtonString => _stringLocalizer.GetString("Settings_WebView_ClearConfirmButton");
    private string CancelString => _stringLocalizer.GetString("DialogButton_Cancel");

    public WebViewSettingsViewModel ViewModel { get; }

    public WebViewSettingsView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<WebViewSettingsViewModel>();

        this.InitializeComponent();
    }
}
