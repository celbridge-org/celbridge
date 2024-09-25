using Celbridge.Status.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Status.Views;

public partial class StatusPanel : UserControl, IStatusPanel
{
    private const string SaveGlyph = "\ue74e";

    private readonly IStringLocalizer _stringLocalizer;

    public StatusPanelViewModel ViewModel { get; }

    public StatusPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ViewModel = serviceProvider.GetRequiredService<StatusPanelViewModel>();

        Loaded += (s, e) => ViewModel.OnLoaded();
        Unloaded += (s, e) => ViewModel.OnUnloaded();

        var fontFamily = ThemeResource.Get<FontFamily>("SymbolThemeFontFamily");

        var selectDocumentButton = new Button()
            .Grid(column: 1)
            .VerticalAlignment(VerticalAlignment.Center)
            .Visibility(x => x.Binding(() => ViewModel.SelectedDocumentVisibility).Mode(BindingMode.OneWay))
            .Command(x => x.Binding(() => ViewModel.SelectDocumentResourceCommand))
            .Content
            (
                 new TextBlock()
                 .Text(x => x.Binding(() => ViewModel.SelectedDocument).Mode(BindingMode.OneWay))
            );

        // Set tooltip for select document button
        var tooltip = _stringLocalizer.GetString("StatusPanel_SelectResourceTooltip");
        ToolTipService.SetToolTip(selectDocumentButton, tooltip);
        ToolTipService.SetPlacement(selectDocumentButton, PlacementMode.Top);

        var copyDocumentButton = new Button()
            .Grid(column: 2)
            .Command(x => x.Binding(() => ViewModel.CopyDocumentResourceCommand))
            .Margin(4)
            .Visibility(x => x.Binding(() => ViewModel.SelectedDocumentVisibility).Mode(BindingMode.OneWay))
            .Content
            (
                new StackPanel()
                .Orientation(Orientation.Horizontal)
                .Children
                (
                    new SymbolIcon()
                    .Symbol(Symbol.Copy)
                )
            );

        var tooltipString = _stringLocalizer.GetString("StatusPanel_CopyResourceKey");
        ToolTipService.SetToolTip(copyDocumentButton, tooltipString);

        var saveIcon = new FontIcon()
            .Grid(column: 3)
            .HorizontalAlignment(HorizontalAlignment.Right)
            .Opacity(x => x.Binding(() => ViewModel.SaveIconOpacity))
            .FontFamily(fontFamily)
            .Glyph(SaveGlyph);

        var panelGrid = new Grid()
            .ColumnDefinitions("48, Auto, Auto, Auto, *")
            .VerticalAlignment(VerticalAlignment.Center)
            .Children
            (
                selectDocumentButton,
                copyDocumentButton,
                saveIcon
            );

        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));
    }
}
