using Celbridge.Inspector.ViewModels;
using Microsoft.UI.Text;

namespace Celbridge.Inspector.Views;

public partial class Inspector : UserControl, IInspector
{
    public InspectorViewModel ViewModel => (DataContext as InspectorViewModel)!;

    private FontIcon? _fontIcon;
    private TextBlock? _resourceNameText;

    public Inspector()
    {
        // Construct the UI elements when the DataContext is populated via the factory.
        DataContextChanged += (s, e) => PopulateContent();
    }

    private void PopulateContent()
    {
        if (ViewModel.Resource.IsEmpty)
        {
            Content = null;
            return;
        }

        _fontIcon = new FontIcon()
            .Glyph(x => x.Binding(() => ViewModel.Icon.FontCharacter))
            .Foreground(x => x.Binding(() => ViewModel.Icon.FontColor))
            .FontFamily(StaticResource.Get<FontFamily>(ViewModel.Icon.FontFamily))
            .FontSize(20)
            .MinWidth(20)
            .Margin(4)
            .VerticalAlignment(VerticalAlignment.Center);

        _resourceNameText = new TextBlock()
            .FontWeight(FontWeights.Bold)
            .IsTextSelectionEnabled(true)
            .VerticalAlignment(VerticalAlignment.Top)
            .Margin(2, 4, 0, 0)
            .Text(x => x.Binding(() => ViewModel.Resource.ResourceName));

        if (!string.IsNullOrEmpty(ViewModel.Resource))
        {
            ToolTipService.SetPlacement(_resourceNameText, PlacementMode.Bottom);
            ToolTipService.SetToolTip(_resourceNameText, ViewModel.Resource);
        }

        var stackPanel = new StackPanel()
            .Orientation(Orientation.Horizontal)
            .Children(_fontIcon, _resourceNameText);

        Content = stackPanel;
    }
}
