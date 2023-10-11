using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Views
{
    public sealed partial class AddResourceDialog : ContentDialog
    {
        public AddResourceViewModel ViewModel { get; set; }

        public AddResourceDialog()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App).Host.Services.GetRequiredService<AddResourceViewModel>();
        }
    }
}
