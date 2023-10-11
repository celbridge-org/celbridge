using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Views
{
    public sealed partial class DetailPanel : UserControl
    {
        public DetailViewModel ViewModel { get; }

        public DetailPanel()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App)!.Host!.Services.GetRequiredService<DetailViewModel>();
            ViewModel.ItemCollection = DetailPropertyListView.Items;
        }
    }
}
