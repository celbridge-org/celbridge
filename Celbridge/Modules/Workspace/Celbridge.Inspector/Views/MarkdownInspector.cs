using Celbridge.Inspector.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Controls;

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

        this.DataContext<WebInspectorViewModel>((inspector, vm) => inspector
            .Content
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
                            .Spacing(2)
                            .Children
                            (
                                new Button()
                                    .Grid(column: 1)
                                    .Margin(2, 0, 2, 0)
                                    .HorizontalAlignment(HorizontalAlignment.Center)
                                    .Command(ViewModel.OpenDocumentCommand)
                                    .ToolTipService(null, null, "Open document")
                                    .Content
                                    (
                                        new StackPanel()
                                            .Orientation(Orientation.Horizontal)
                                            .Children(
                                                new SymbolIcon()
                                                    .Symbol(Symbol.OpenFile)
                                                    .Margin(8, 0, 0, 0)
                                            )
                                    ),
                                new Button()
                                    .HorizontalAlignment(HorizontalAlignment.Center)
                                    .ToolTipService(null, null, "Show editor panel")
                                    .Content
                                    (
                                        new StackPanel()
                                            .Orientation(Orientation.Horizontal)
                                            .Children(
                                                new SymbolIcon()
                                                    .Symbol(Symbol.Document)
                                                    .Margin(8, 0, 0, 0)
                                            )
                                    ),
                                new Button()
                                    .HorizontalAlignment(HorizontalAlignment.Center)
                                    .ToolTipService(null, null, "Show preview panel")
                                    .Content
                                    (
                                        new StackPanel()
                                            .Orientation(Orientation.Horizontal)
                                            .Children(
                                                new SymbolIcon()
                                                    .Symbol(Symbol.PreviewLink)
                                                    .Margin(8, 0, 0, 0)
                                            )
                                    )
                            )
                    )
            ));
    }
}
