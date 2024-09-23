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

        var selectedDocumentButton = new Button()
            .Grid(column: 1)
            .Margin(4)
            .Command(x => x.Binding(() => ViewModel.CopyDocumentResourceCommand))
            .Visibility(x => x.Binding(() => ViewModel.SelectedDocumentVisibility)
                              .Mode(BindingMode.OneWay))
            .Content
            (
                new TextBlock()
                    .Text(x => x.Binding(() => ViewModel.SelectedDocument)
                                .Mode(BindingMode.OneWay))
            );

        var tooltipString = _stringLocalizer.GetString("StatusPanel_CopyResourceKey");
        ToolTipService.SetToolTip(selectedDocumentButton, tooltipString);

        var saveIcon = new FontIcon()
            .Grid(column: 2)
            .HorizontalAlignment(HorizontalAlignment.Right)
            .Opacity(x => x.Binding(() => ViewModel.SaveIconOpacity))
            .FontFamily(fontFamily)
            .Glyph(SaveGlyph);

        var panelGrid = new Grid()
            .ColumnDefinitions("48, Auto, Auto, *")
            .VerticalAlignment(VerticalAlignment.Center)
            .Children
            (
                selectedDocumentButton,
                saveIcon
            );

        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));
    }
}
