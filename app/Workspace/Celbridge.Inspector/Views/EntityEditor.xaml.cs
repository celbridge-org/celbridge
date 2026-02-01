using Celbridge.Inspector.ViewModels;

namespace Celbridge.Inspector.Views;

public sealed partial class EntityEditor : UserControl
{
    public EntityEditorViewModel ViewModel { get; set; }

    // Splitter drag state
    private double _componentListStartHeight;
    private double _detailStartHeight;

    public EntityEditor()
    {
        this.InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<EntityEditorViewModel>();
        DataContext = ViewModel;

        Loaded += EntityEditor_Loaded;
        Unloaded += EntityEditor_Unloaded;
    }

    private void EntityEditor_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.InspectedComponentChanged += ViewModel_InspectedComponentChanged;

        // Set up splitter event handlers
        DetailSplitter.DragStarted += DetailSplitter_DragStarted;
        DetailSplitter.DragDelta += DetailSplitter_DragDelta;
    }

    private void EntityEditor_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.InspectedComponentChanged -= ViewModel_InspectedComponentChanged;

        // Clean up splitter event handlers
        DetailSplitter.DragStarted -= DetailSplitter_DragStarted;
        DetailSplitter.DragDelta -= DetailSplitter_DragDelta;
    }

    private void ViewModel_InspectedComponentChanged()
    {
        DetailScrollViewer.ScrollToVerticalOffset(0);
    }

    public void ClearComponentListPanel()
    {
        ComponentListPanel.Children.Clear();
    }

    public void PopulateComponentsPanel(List<UIElement> elements)
    {
        ClearComponentListPanel();
        foreach (UIElement element in elements)
        {
            ComponentListPanel.Children.Add(element);
        }
    }

    //
    // Splitter event handlers for panel resizing
    //

    private void DetailSplitter_DragStarted(object? sender, EventArgs e)
    {
        // Get the Grid's row definitions to access actual heights
        var grid = this.Content as Grid;
        if (grid != null && grid.RowDefinitions.Count >= 3)
        {
            _componentListStartHeight = grid.RowDefinitions[1].ActualHeight;
            _detailStartHeight = grid.RowDefinitions[2].ActualHeight;
        }
    }

    private void DetailSplitter_DragDelta(object? sender, double delta)
    {
        var grid = this.Content as Grid;
        if (grid == null || grid.RowDefinitions.Count < 3)
        {
            return;
        }

        var componentRow = grid.RowDefinitions[1];
        var detailRow = grid.RowDefinitions[2];

        // Dragging down increases component list, decreases detail panel
        var newComponentHeight = _componentListStartHeight + delta;
        var newDetailHeight = _detailStartHeight - delta;

        // Enforce minimum heights (100px each)
        const double minHeight = 100;
        if (newComponentHeight >= minHeight && newDetailHeight >= minHeight)
        {
            componentRow.Height = new GridLength(newComponentHeight, GridUnitType.Pixel);
            detailRow.Height = new GridLength(newDetailHeight, GridUnitType.Pixel);
        }
    }
}
