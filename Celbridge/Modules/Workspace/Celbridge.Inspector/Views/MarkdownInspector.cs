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

        this.DataContext<MarkdownInspectorViewModel>((inspector, vm) => inspector
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
                                            )
                                    )
                            )
                    )
            ));
    }
}
