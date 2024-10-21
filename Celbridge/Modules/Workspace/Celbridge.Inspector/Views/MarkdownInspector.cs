using Celbridge.Inspector.ViewModels;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using Windows.UI;

namespace Celbridge.Inspector.Views;

public partial class MarkdownInspector : UserControl, IInspector
{
    private readonly IStringLocalizer _stringLocalizer;

    public MarkdownInspectorViewModel ViewModel => (DataContext as MarkdownInspectorViewModel)!;

    private LocalizedString StartURLString => _stringLocalizer.GetString("WebInspector_StartURL");

    public ResourceKey Resource 
    {
        set => ViewModel.Resource = value; 
        get => ViewModel.Resource; 
    }

    private ComponentListView? _componentListView;

    // Code gen requires a parameterless constructor
    public MarkdownInspector()
    {
        throw new NotImplementedException();
    }

    public MarkdownInspector(MarkdownInspectorViewModel viewModel)
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        DataContext = viewModel;

        Button? showComponentMockupButton = null;

        this.DataContext<MarkdownInspectorViewModel>((inspector, vm) => inspector
            .Content
            (
                new StackPanel()
                    .Orientation(Orientation.Vertical)
                    .Children
                    (
                        new Grid()
                            .HorizontalAlignment(HorizontalAlignment.Stretch)
                            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
                            .BorderThickness(0, 1, 0, 1)
                            .Children
                            (
                                new StackPanel()
                                    .Orientation(Orientation.Horizontal)
                                    .HorizontalAlignment(HorizontalAlignment.Center)
                                    .Children
                                    (
                                        new Button()
                                            .Margin(2, 2, 8, 0)
                                            .HorizontalAlignment(HorizontalAlignment.Center)
                                            .Command(() => vm.OpenDocumentCommand)
                                            .ToolTipService(null, null, "Open document")
                                            .Content
                                            (
                                                new SymbolIcon()
                                                    .Symbol(Symbol.OpenFile)
                                            ),
                                        new StackPanel()
                                            .Orientation(Orientation.Horizontal)
                                            .HorizontalAlignment(HorizontalAlignment.Center)
                                            .VerticalAlignment(VerticalAlignment.Center)
                                            .Children(
                                                new Button()
                                                    .Margin(2)
                                                    .Command(() => vm.ShowEditorCommand)
                                                    .IsEnabled(x => x.Binding(() => vm.ShowEditorEnabled)
                                                        .Mode(BindingMode.OneWay))
                                                    .ToolTipService(null, null, "Show editor panel only")
                                                    .Content
                                                    (
                                                        new SymbolIcon()
                                                            .Symbol(Symbol.DockRight)
                                                    ),
                                                new Button()
                                                    .Margin(2)
                                                    .Command(() => vm.ShowBothCommand)
                                                    .IsEnabled(x => x.Binding(() => vm.ShowBothEnabled)
                                                        .Mode(BindingMode.OneWay))
                                                    .ToolTipService(null, null, "Show both editor and preview panels")
                                                    .Content
                                                    (
                                                        new SymbolIcon()
                                                            .Symbol(Symbol.DockBottom)
                                                    ),
                                                new Button()
                                                    .Margin(2)
                                                    .Command(() => vm.ShowPreviewCommand)
                                                    .IsEnabled(x => x.Binding(() => vm.ShowPreviewEnabled)
                                                        .Mode(BindingMode.OneWay))
                                                    .ToolTipService(null, null, "Show preview panel only")
                                                    .Content
                                                    (
                                                        new SymbolIcon()
                                                            .Symbol(Symbol.DockLeft)
                                                    ),
                                                new Button()
                                                    .Name(out showComponentMockupButton)
                                                    .Margin(8, 2, 2, 2)
                                                    .ToolTipService(null, null, "Add component")
                                                    .Content
                                                    (
                                                        new SymbolIcon()
                                                            .Symbol(Symbol.Add)
                                                    )
                                            )
                                    )
                            ),
                        new ComponentListView()
                            .Name(out _componentListView)
                    )
            ));

        if (showComponentMockupButton is not null)
        {
            showComponentMockupButton.Click += ShowComponentMockupButton_Click;
        }
    }

    private void ShowComponentMockupButton_Click(object sender, RoutedEventArgs e)
    {
        var listViewItem = new ListViewItem()
            .Content
            (
                new Grid()
                    .ColumnDefinitions("*, 2*, auto")
                    .Children
                    (
                        new TextBox()
                            .Grid(column: 0)
                            .VerticalAlignment(VerticalAlignment.Center)
                            .Text("VoiceLine"),
                        new TextBlock()
                            .Grid(column: 1)
                            .Margin(8, 0, 0, 0)
                            .VerticalAlignment(VerticalAlignment.Center)
                            .Text("Darth Vader: No, I am your father!"),
                        new SymbolIcon()
                            .Grid(column: 2)
                            .Symbol(Symbol.Play)
                            .ToolTipService(null, null, "Play using text to speech")
                    )
            );

        Guard.IsNotNull(_componentListView);
        _componentListView.AddItem(listViewItem);
    }

    private SolidColorBrush ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');

        byte a = 255; // Default alpha value
        byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
        byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
        byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);

        if (hex.Length == 8)
        {
            a = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
        }

        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }

}
