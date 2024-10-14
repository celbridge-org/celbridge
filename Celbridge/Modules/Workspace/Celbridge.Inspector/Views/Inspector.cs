using Celbridge.Inspector.ViewModels;
using Microsoft.UI.Text;

namespace Celbridge.Inspector.Views;

public partial class Inspector : UserControl, IInspector
{
    public InspectorViewModel ViewModel => (DataContext as InspectorViewModel)!;

    private TextBlock _resourceNameText;

    public Inspector()
    {
        _resourceNameText = new TextBlock()
            .Grid(row: 0)
            .FontWeight(FontWeights.Bold)
            .IsTextSelectionEnabled(true)
            .Margin(6)
            .Text(x => x.Binding(() => ViewModel.Resource.ResourceName)
                .Mode(BindingMode.OneWay));

        DataContextChanged += Inspector_DataContextChanged;

        Content = _resourceNameText;
    }

    private void Inspector_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(ViewModel.Resource))
        {
            ToolTipService.SetToolTip(_resourceNameText, null);
        }
        else
        {
            ToolTipService.SetPlacement(_resourceNameText, PlacementMode.Bottom);
            ToolTipService.SetToolTip(_resourceNameText, ViewModel.Resource);
        }
    }
}
