using Celbridge.Utils;
using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Views
{
    public partial class NumberPropertyView : UserControl, IPropertyView
    {
        public NumberPropertyViewModel ViewModel { get; }

        public NumberPropertyView()
        {
            this.InitializeComponent();

            var services = (Application.Current as App)!.Host!.Services;
            ViewModel = services.GetRequiredService<NumberPropertyViewModel>();
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
