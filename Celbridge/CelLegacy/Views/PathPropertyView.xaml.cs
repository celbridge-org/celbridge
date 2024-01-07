using Celbridge.ViewModels;
using Celbridge.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Celbridge.Views
{
    public partial class PathPropertyView : UserControl, IPropertyView
    {
        public PathPropertyViewModel ViewModel { get; }

        public PathPropertyView()
        {
            this.InitializeComponent();

            var services = LegacyServiceProvider.Services!;
            ViewModel = services.GetRequiredService<PathPropertyViewModel>();
        }

        public void SetProperty(Property property, string labelText)
        {
            ViewModel.SetProperty(property, labelText);
        }

        public int ItemIndex
        {
            get => ViewModel.ItemIndex;
            set => ViewModel.ItemIndex = value;
        }

        public Result CreateChildViews()
        {
            return new SuccessResult();
        }
    }
}
