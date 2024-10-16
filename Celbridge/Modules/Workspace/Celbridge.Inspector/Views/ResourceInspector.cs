using Celbridge.Inspector.ViewModels;
using Microsoft.UI.Text;

namespace Celbridge.Inspector.Views;

public partial class ResourceInspector : UserControl, IInspector
{
    public ResourceInspectorViewModel ViewModel => (DataContext as ResourceInspectorViewModel)!;

    public ResourceKey Resource 
    {
        set => ViewModel.Resource = value; 
        get => ViewModel.Resource; 
    }

    private FontIcon? _fontIcon;
    private TextBlock? _resourceNameText;

    // Code gen requires a parameterless constructor
    public ResourceInspector()
    {
        throw new NotImplementedException();
    }

    public ResourceInspector(ResourceInspectorViewModel viewModel)
    {
        DataContext = viewModel;

        this.DataContext<ResourceInspectorViewModel>((inspector, vm) => inspector
            .Content(
                new StackPanel()
                    .Orientation(Orientation.Horizontal)
                    .Children(
                        new FontIcon()
                            .Name(out _fontIcon)
                            .Glyph(x => x.Binding(() => vm.Icon.FontCharacter)
                                .Mode(BindingMode.OneWay))
                            .Foreground(x => x.Binding(() => vm.Icon.FontColor)
                                .Mode(BindingMode.OneWay))
                            .FontSize(20)
                            .FontFamily(StaticResource.Get<FontFamily>(ViewModel.Icon.FontFamily))                                
                            .MinWidth(20)
                            .Margin(4)
                            .VerticalAlignment(VerticalAlignment.Center),
                        new TextBlock()
                            .Name(out _resourceNameText)
                            .FontWeight(FontWeights.Bold)
                            .IsTextSelectionEnabled(true)
                            .VerticalAlignment(VerticalAlignment.Top)
                            .Margin(2, 4, 0, 0)
                            .Text(x => x.Binding(() => vm.Resource.ResourceName)
                                .Mode(BindingMode.OneWay))
                    )
            )
        );

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.Resource))
        {
            if (!ViewModel.Resource.IsEmpty)
            {
                var fontFamily = (FontFamily)Application.Current.Resources[ViewModel.Icon.FontFamily];
                _fontIcon!.FontFamily = fontFamily;

                ToolTipService.SetPlacement(_resourceNameText, PlacementMode.Bottom);
                ToolTipService.SetToolTip(_resourceNameText, ViewModel.Resource);
            }
        }
    }
}
