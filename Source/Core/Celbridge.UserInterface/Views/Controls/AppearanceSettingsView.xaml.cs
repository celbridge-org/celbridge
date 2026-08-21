using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The Appearance section of the settings dialog.
/// </summary>
public sealed partial class AppearanceSettingsView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private string ApplicationThemeString => _stringLocalizer.GetString("Settings_Appearance_Theme");

    public AppearanceSettingsViewModel ViewModel { get; }

    public AppearanceSettingsView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<AppearanceSettingsViewModel>();

        this.InitializeComponent();

        // The dialog swaps the selected section in and out of its content host, so these fire each time
        // the user comes back to this section rather than once per dialog. They stay attached for the
        // lifetime of the control.
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnShown();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnHidden();
    }
}
