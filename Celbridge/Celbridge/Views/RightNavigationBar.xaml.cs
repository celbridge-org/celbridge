using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Views
{
    public sealed partial class RightNavigationBar : UserControl
    {
        public RightNavigationBarViewModel ViewModel { get; set; }

        public RightNavigationBar()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App)!.Host!.Services.GetRequiredService<RightNavigationBarViewModel>();

            Loaded += Page_Loaded;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Expanded")
            {
            }
        }

        private void Page_Loaded(object? sender, RoutedEventArgs e)
        {
            ViewModel.ShellRoot = this.XamlRoot;
        }
    }
}
