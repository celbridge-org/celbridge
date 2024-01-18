namespace CelLegacy.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    public SettingsViewModel ViewModel { get; }

    public SettingsDialog()
    {
        this.InitializeComponent();
        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<SettingsViewModel>();
    }
}
