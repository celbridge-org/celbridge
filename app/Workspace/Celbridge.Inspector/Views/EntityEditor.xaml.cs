using Celbridge.Inspector.ViewModels;
using Celbridge.UserInterface.Helpers;

namespace Celbridge.Inspector.Views;

public sealed partial class EntityEditor : UserControl
{
    public EntityEditorViewModel ViewModel { get; set; }

    private SplitterHelper? _splitterHelper;

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

        // Initialize splitter helper
        var grid = this.Content as Grid;
        if (grid != null)
        {
            _splitterHelper = new SplitterHelper(grid, GridResizeMode.Rows, 1, 2, minSize: 100);
        }

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
        _splitterHelper?.OnDragStarted();
    }

    private void DetailSplitter_DragDelta(object? sender, double delta)
    {
        _splitterHelper?.OnDragDelta(delta);
    }
}
