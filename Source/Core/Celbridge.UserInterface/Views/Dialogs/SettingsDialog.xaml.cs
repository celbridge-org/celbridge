using Celbridge.Dialog;
using Celbridge.UserInterface.ViewModels.Dialogs;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The application settings dialog. Each section is a self-contained control composed into it.
/// </summary>
public sealed partial class SettingsDialog : ContentDialog, ISettingsDialog
{
    private readonly IStringLocalizer _stringLocalizer;

    private string TitleString => _stringLocalizer.GetString("Settings_Page_Title");
    private string CloseString => _stringLocalizer.GetString("DialogButton_Close");
    private string ApplicationThemeString => _stringLocalizer.GetString("Settings_Application_Theme");

    public SettingsDialogViewModel ViewModel { get; }

    public SettingsDialog()
    {
        // The labels are bound one-time, so the localizer has to be in place before the XAML loads.
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<SettingsDialogViewModel>();

        this.InitializeComponent();

        this.EnableThemeSync();
    }

    public async Task ShowDialogAsync()
    {
        ViewModel.OnOpened();
        try
        {
            await ShowAsync();
        }
        finally
        {
            ViewModel.OnClosed();
        }
    }
}
