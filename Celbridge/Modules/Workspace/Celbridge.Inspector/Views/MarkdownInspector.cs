using Celbridge.Inspector.ViewModels;
using Microsoft.Extensions.Localization;

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
                new StackPanel()
                    .Orientation(Orientation.Vertical)
                    .Children
                    (
                        new Button()
                            .Grid(column: 1)
                            .Margin(2, 0, 2, 0)
                            .VerticalAlignment(VerticalAlignment.Bottom)
                            .HorizontalAlignment(HorizontalAlignment.Center)
                            .Command(ViewModel.OpenDocumentCommand)
                            .ToolTipService(null, null, inspector.StartURLString)
                            .IsEnabled(x => x.Binding(() => vm.SourceUrl)
                                .Mode(BindingMode.OneWay)
                                .Convert((url) => !string.IsNullOrWhiteSpace(url)))
                            .Content
                            (
                                new StackPanel()
                                    .Orientation(Orientation.Horizontal)
                                    .ToolTipService(null, null, "Open document")
                                    .Children(
                                        new TextBlock()
                                            .Text("Open"),
                                        new SymbolIcon()
                                            .Symbol(Symbol.OpenFile)
                                            .Margin(8, 0, 0, 0)
                                    )
                            )
                    )
            ));
    }
}
