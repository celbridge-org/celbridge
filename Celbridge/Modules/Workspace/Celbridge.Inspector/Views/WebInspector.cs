using Celbridge.Inspector.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Inspector.Views;

public partial class WebInspector : UserControl, IInspector
{
    private readonly IStringLocalizer _stringLocalizer;

    public WebInspectorViewModel ViewModel => (DataContext as WebInspectorViewModel)!;
    private LocalizedString StartURLString => _stringLocalizer.GetString("WebInspector_StartURL");
    private LocalizedString OpenURLTooltipString => _stringLocalizer.GetString("WebInspector_OpenURLTooltip");

    public ResourceKey Resource 
    {
        set => ViewModel.Resource = value; 
        get => ViewModel.Resource; 
    }

    // Code gen requires a parameterless constructor
    public WebInspector()
    {
        throw new NotImplementedException();
    }

    public WebInspector(WebInspectorViewModel viewModel)
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
                        new Grid()
                            .ColumnDefinitions("*, auto")
                            .HorizontalAlignment(HorizontalAlignment.Stretch)
                            .Children
                            (
                                new TextBox()
                                    .Grid(column: 0)
                                    // This appears to be the right way to bind to a localized string.
                                    // Just doing .Header(StartURLString) throws a cryptic XAML exception.
                                    // https://platform.uno/docs/articles/external/uno.extensions/doc/Learn/Markup/Binding101.html
                                    .Header(() => inspector.StartURLString)
                                    .HorizontalAlignment(HorizontalAlignment.Stretch)
                                    .Text(x => x.Binding(() => vm.Url)
                                        .Mode(BindingMode.TwoWay)),
                                new Button()
                                    .Grid(column: 1)
                                    .Margin(2, 0, 2, 0)
                                    .VerticalAlignment(VerticalAlignment.Bottom)
                                    .Command(ViewModel.RefreshCommand)
                                    // The binding technique above doesn't work for tooltips though.
                                    // In this case you seem to have to reference the string directly.
                                    .ToolTipService(null, null, inspector.OpenURLTooltipString)
                                    .Content
                                    (
                                        new SymbolIcon()
                                            .Symbol(Symbol.Forward)
                                    )
                            )
                    )
            ));
    }
}
