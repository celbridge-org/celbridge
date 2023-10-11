using Celbridge.ViewModels;

namespace Celbridge.Views
{
    public sealed partial class StatusBar : UserControl
    {
        public StatusBarViewModel ViewModel { get; set; }

        public StatusBar()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App)!.Host!.Services.GetRequiredService<StatusBarViewModel>();
        }
    }
}
