using Celbridge.Utils;
using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Views
{
    public partial class RecordSummaryPropertyView : UserControl, IPropertyView
    {
        public RecordSummaryPropertyViewModel ViewModel { get; }

        public RecordSummaryPropertyView()
        {
            this.InitializeComponent();

            var services = (Application.Current as App)!.Host!.Services;
            ViewModel = services.GetRequiredService<RecordSummaryPropertyViewModel>();
        }

        public void SetProperty(Property property, string _)
        {
            ViewModel.SetProperty(property);
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
