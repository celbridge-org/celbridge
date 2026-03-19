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
        _splitterHelper = new SplitterHelper(RootGrid, GridResizeMode.Rows, 1, 2, minSize: 100);

        // Set up splitter event handlers
        DetailSplitter.DragStarted += DetailSplitter_DragStarted;
        DetailSplitter.DragDelta += DetailSplitter_DragDelta;
        DetailSplitter.DoubleClicked += DetailSplitter_DoubleClicked;
    }

    private void EntityEditor_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.InspectedComponentChanged -= ViewModel_InspectedComponentChanged;

        // Clean up splitter event handlers
        DetailSplitter.DragStarted -= DetailSplitter_DragStarted;
        DetailSplitter.DragDelta -= DetailSplitter_DragDelta;
        DetailSplitter.DoubleClicked -= DetailSplitter_DoubleClicked;
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

    private void DetailSplitter_DoubleClicked(object? sender, EventArgs e)
    {
        // Reset both rows to equal Star sizing (same as initial XAML layout)
        RootGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        RootGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
    }
}
