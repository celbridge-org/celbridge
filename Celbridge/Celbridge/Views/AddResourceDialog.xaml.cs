using Celbridge.ViewModels;

namespace Celbridge.Views
{
    public sealed partial class AddResourceDialog : ContentDialog
    {
        public AddResourceViewModel ViewModel { get; set; }

        public AddResourceDialog()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App)!.Host!.Services.GetRequiredService<AddResourceViewModel>();
        }
    }
}
