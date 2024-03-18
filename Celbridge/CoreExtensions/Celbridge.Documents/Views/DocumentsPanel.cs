using Celbridge.Documents.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

public sealed partial class DocumentsPanel : UserControl
{
    public LocalizedString Title => _stringLocalizer.GetString($"{nameof(DocumentsPanel)}_{nameof(Title)}");

    private IStringLocalizer _stringLocalizer;

    private ColumnDefinition _spacerColumn;

    public DocumentsPanelViewModel ViewModel { get; }

    public DocumentsPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ViewModel = serviceProvider.GetRequiredService<DocumentsPanelViewModel>();

        var titleBar = new Grid()
            .Background(ThemeResource.Get<Brush>("PanelBackgroundBrush"))
            .BorderBrush(ThemeResource.Get<Brush>("PanelBorderBrush"))
            .BorderThickness(0, 1, 0, 1)
            .ColumnDefinitions("96, Auto, *, 48")
            .Children(
                new TextBlock()
                    .Grid(column: 1)
                    .Text(Title)
                    .Margin(6, 0, 0, 0)
                    .VerticalAlignment(VerticalAlignment.Center)
            );

        _spacerColumn = titleBar.ColumnDefinitions[0];

        var panelGrid = new Grid()
            .RowDefinitions("40, *")
            .Children(titleBar);
           
        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(panelGrid));

        // Listen for property changes on the ViewModel
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += DocumentsPanel_Loaded;
        Unloaded += DocumentsPanel_Unloaded;
    }

    private void DocumentsPanel_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnViewLoaded();
    }

    private void DocumentsPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.OnViewUnloaded();

        Loaded -= DocumentsPanel_Loaded;
        Unloaded -= DocumentsPanel_Unloaded;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsLeftPanelVisible))
        {
            // The spacer column offsets the document tabs to the right to avoid overlapping with the main menu button.
            // This offset is only necessary when the left workspace panel is collapsed.
            var columnWidth = ViewModel.IsLeftPanelVisible ? 0 : 96;
            _spacerColumn.Width = new GridLength(columnWidth);
        }
    }
}
