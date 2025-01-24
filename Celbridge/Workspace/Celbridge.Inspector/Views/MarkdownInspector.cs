using Celbridge.Documents;
using Celbridge.Inspector.ViewModels;
using Microsoft.Extensions.Localization;
using Windows.UI;

namespace Celbridge.Inspector.Views;

public partial class MarkdownInspector : UserControl, IInspector
{
    private readonly IStringLocalizer _stringLocalizer;

    public MarkdownInspectorViewModel ViewModel => (DataContext as MarkdownInspectorViewModel)!;

    private LocalizedString StartURLString => _stringLocalizer.GetString("WebInspector_StartURL");
    private LocalizedString OpenDocumentTooltipString => _stringLocalizer.GetString("InspectorPanel_OpenDocumentTooltip");
    private LocalizedString ShowEditorTooltipString => _stringLocalizer.GetString("InspectorPanel_ShowEditorTooltip");
    private LocalizedString ShowPreviewTooltipString => _stringLocalizer.GetString("InspectorPanel_ShowPreviewTooltip");
    private LocalizedString ShowEditorAndPreviewTooltipString => _stringLocalizer.GetString("InspectorPanel_ShowEditorAndPreviewTooltip");

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
                                            .ToolTipService(null, null, OpenDocumentTooltipString)
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
                                                    .IsEnabled(x => x.Binding(() => vm.EditorMode)
                                                        .Mode(BindingMode.OneWay)
                                                        .Convert(editMode => editMode != EditorMode.Editor))
                                                    .ToolTipService(null, null, ShowEditorTooltipString)
                                                    .Content
                                                    (
                                                        new SymbolIcon()
                                                            .Symbol(Symbol.DockRight)
                                                    ),
                                                new Button()
                                                    .Margin(2)
                                                    .Command(() => vm.ShowBothCommand)
                                                    .IsEnabled(x => x.Binding(() => vm.EditorMode)
                                                        .Mode(BindingMode.OneWay)
                                                        .Convert(editMode => editMode != EditorMode.EditorAndPreview))
                                                    .ToolTipService(null, null, ShowEditorAndPreviewTooltipString)
                                                    .Content
                                                    (
                                                        new SymbolIcon()
                                                            .Symbol(Symbol.DockBottom)
                                                    ),
                                                new Button()
                                                    .Margin(2)
                                                    .Command(() => vm.ShowPreviewCommand)
                                                    .IsEnabled(x => x.Binding(() => vm.EditorMode)
                                                        .Mode(BindingMode.OneWay)
                                                        .Convert(editMode => editMode != EditorMode.Preview))
                                                    .ToolTipService(null, null, ShowPreviewTooltipString)
                                                    .Content
                                                    (
                                                        new SymbolIcon()
                                                            .Symbol(Symbol.DockLeft)
                                                    )
                                            )
                                    )
                            )
                    )
            ));
    }
}
