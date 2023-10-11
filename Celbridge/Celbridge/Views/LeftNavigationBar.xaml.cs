using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.System;

namespace Celbridge.Views
{
    public sealed partial class LeftNavigationBar : UserControl
    {
        public LeftNavigationBarViewModel ViewModel { get; set; }

        public LeftNavigationBar()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App).Host.Services.GetRequiredService<LeftNavigationBarViewModel>();

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Expanded")
            {
            }
        }
    }
}
