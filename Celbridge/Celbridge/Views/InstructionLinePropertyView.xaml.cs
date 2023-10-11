using Celbridge.Utils;
using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace Celbridge.Views
{
    // Ideally this class would inherit from RecordSummaryPropertyView, but that's not supported by XAML
    // so we just have two separate classes. The view models do inherit from each other however.
    public partial class InstructionLinePropertyView : UserControl, IPropertyView
    {
        public InstructionLinePropertyViewModel ViewModel { get; }

        public InstructionLinePropertyView()
        {
            this.InitializeComponent();

            var services = (Application.Current as App).Host.Services;
            ViewModel = services.GetRequiredService<InstructionLinePropertyViewModel>();

            ViewModel.KeywordTextBox = KeywordTextBox;
            // The ViewModel populates the TextBlock by building the description from colored inline Run elements.
            ViewModel.DescriptionTextBlock = this.DescriptionTextBlock;

            // Reordering list items loads and unloads the contained view several times.
            // While unloaded, the ViewModel should not receive certain events, so we need to inform the ViewModel
            // when the loaded state changes.
            Loaded += (s, e) => ViewModel.OnViewLoaded();
            Unloaded += (s, e) => ViewModel.OnViewUnloaded();
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

        public void NotifyWillDelete() 
        {
            ViewModel.NotifyWillDelete();
        }

        public void NotifyIndexChanged(int newIndex) 
        { 
            ViewModel.NotifyIndexChanged(newIndex);
        }

        public Result CreateChildViews()
        {
            return new SuccessResult();
        }

        private void KeywordTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ViewModel.OnGotFocus();
        }
    }
}
