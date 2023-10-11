using Celbridge.ViewModels;

namespace Celbridge.Views
{
    public sealed partial class NewProjectDialog : ContentDialog
    {
        public NewProjectViewModel ViewModel { get; set; }
        
        public NewProjectDialog()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App)!.Host!.Services.GetRequiredService<NewProjectViewModel>();
        }
    }
}
