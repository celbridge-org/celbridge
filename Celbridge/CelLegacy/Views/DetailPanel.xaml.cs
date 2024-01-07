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
            ViewModel = LegacyServiceProvider.Services!.GetRequiredService<DetailViewModel>();
            ViewModel.ItemCollection = DetailPropertyListView.Items;
        }
    }
}
