using Celbridge.ViewModels;

namespace Celbridge.Views
{
    public sealed partial class SettingsDialog : ContentDialog
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsDialog()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App)!.Host!.Services.GetRequiredService<SettingsViewModel>();
        }
    }

}
