using Celbridge.Inspector.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Inspector.Views;

public partial class WebInspector : UserControl, IInspector
{
    public WebInspectorViewModel ViewModel => (DataContext as WebInspectorViewModel)!;

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
        var stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        var startURLString = stringLocalizer.GetString("WebInspector_StartURL");

        DataContext = viewModel;

        this.DataContext<WebInspectorViewModel>((inspector, vm) => inspector
            .Content(
                new Grid()
                    .ColumnDefinitions("*, auto") 
                    .HorizontalAlignment(HorizontalAlignment.Stretch)
                    .Children(
                        new TextBox()
                            .Grid(column: 0)
                            .Header(startURLString)
                            .HorizontalAlignment(HorizontalAlignment.Stretch)
                            .Text(x => x.Binding(() => vm.Url)
                                .Mode(BindingMode.TwoWay)),
                        new Button()
                            .Grid(column: 1)
                            .Margin(2, 0, 2, 0)
                            .VerticalAlignment(VerticalAlignment.Bottom)
                            .Command(ViewModel.RefreshCommand)
                            .Content(
                                new SymbolIcon()
                                .Symbol(Symbol.Forward)
                            )
                    )
            )   
        );
    }
}
