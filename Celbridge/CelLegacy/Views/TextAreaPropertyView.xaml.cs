using Celbridge.Utils;
using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Views
{
    public partial class TextAreaPropertyView : UserControl, IPropertyView
    {
        public TextPropertyViewModel ViewModel { get; }

        public TextAreaPropertyView()
        {
            this.InitializeComponent();

            var services = LegacyServiceProvider.Services!;
            ViewModel = services.GetRequiredService<TextPropertyViewModel>();

            // Fixes a bug when the TextBox initializes where it doesn't accept return even
            // though the bound property is set to true. TextBox.AcceptsReturn defaults to false,
            // setting it to true here seems to force it to end up with the correct value.
            ValueTextBox.AcceptsReturn = true;
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
