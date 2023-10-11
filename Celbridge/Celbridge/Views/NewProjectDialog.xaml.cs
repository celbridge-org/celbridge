using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Views
{
    public sealed partial class NewProjectDialog : ContentDialog
    {
        public NewProjectViewModel ViewModel { get; set; }
        
        public NewProjectDialog()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App).Host.Services.GetRequiredService<NewProjectViewModel>();
        }
    }
}
