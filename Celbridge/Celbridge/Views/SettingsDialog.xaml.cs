using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
