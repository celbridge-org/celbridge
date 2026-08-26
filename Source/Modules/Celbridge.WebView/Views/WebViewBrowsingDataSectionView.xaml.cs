using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.UserInterface;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.Views;

/// <summary>
/// The Browsing Data section of the Web View settings: what the shared web profile holds, and the way to
/// the Application Settings that clear it.
/// </summary>
public sealed partial class WebViewBrowsingDataSectionView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    public string BrowsingDataHintString => _stringLocalizer.GetString("WebView_Settings_BrowsingDataHint");
    public string OpenApplicationSettingsString => _stringLocalizer.GetString("WebView_Settings_OpenApplicationSettings");

    public WebViewBrowsingDataSectionView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();

        InitializeComponent();
    }

    private void OpenApplicationSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _commandService.Execute<IShowSettingsCommand>(command =>
        {
            command.SectionKey = SettingsDialogSections.WebView;
        });
    }
}
