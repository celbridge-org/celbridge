using Celbridge.Browser.ViewModels;

namespace Celbridge.Browser.Views;

/// <summary>
/// Moves a tab along the tab bar by dragging it. A press past the drag threshold shows an insertion marker, and the
/// release moves the tab to the marked place. The pointer is tracked on the window's content, because the tab's button
/// captures the pointer on press. Pointer events then go to the button and up through its ancestors.
/// </summary>
internal sealed class BrowserTabDragHandler
{
    private const double DragThreshold = 5.0;

    private readonly ItemsControl _tabItems;
    private readonly Canvas _overlay;
    private readonly FrameworkElement _insertionMarker;
    private readonly PointerEventHandler _pointerMovedHandler;
    private readonly PointerEventHandler _pointerReleasedHandler;
    private readonly PointerEventHandler _pointerCaptureLostHandler;

    private BrowserTabViewModel? _pressedTab;
    private UIElement? _inputRoot;
    private double _pressX;
    private bool _isDragging;
    private int _insertionSlot;

    public BrowserTabDragHandler(ItemsControl tabItems, Canvas overlay, FrameworkElement insertionMarker)
    {
        _tabItems = tabItems;
        _overlay = overlay;
        _insertionMarker = insertionMarker;
        _pointerMovedHandler = OnPointerMoved;
        _pointerReleasedHandler = OnPointerReleased;
        _pointerCaptureLostHandler = OnPointerCaptureLost;
    }

    public BrowserDocumentViewModel? ViewModel { get; set; }

    /// <summary>
    /// Returns true from the moment the pointer passes the drag threshold until the handler hears the release.
    /// </summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    /// Returns the index a tab moves to when dropped in the given slot. Slot N sits before the tab at index N, so a
    /// slot after the tab's own place counts the tab itself.
    /// </summary>
    public static int GetTargetIndex(int currentIndex, int insertionSlot)
    {
        return insertionSlot > currentIndex ? insertionSlot - 1 : insertionSlot;
    }

    /// <summary>
    /// Begins tracking a press on a tab. The drag starts only once the pointer passes the drag threshold, so a plain
    /// click is unaffected.
    /// </summary>
    public void OnTabPressed(BrowserTabViewModel tab, PointerRoutedEventArgs e)
    {
        EndTracking();

        var pointerPoint = e.GetCurrentPoint(_overlay);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var inputRoot = _overlay.XamlRoot?.Content as UIElement;
        if (inputRoot is null)
        {
            return;
        }

        _pressedTab = tab;
        _pressX = pointerPoint.Position.X;
        _inputRoot = inputRoot;

        inputRoot.AddHandler(UIElement.PointerMovedEvent, _pointerMovedHandler, handledEventsToo: true);
        inputRoot.AddHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler, handledEventsToo: true);
        inputRoot.AddHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler, handledEventsToo: true);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedTab is null)
        {
            return;
        }

        var pointerPoint = e.GetCurrentPoint(_overlay);

        // A move with the button up means the release went elsewhere, for example into a web view.
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            EndTracking();
            return;
        }

        var x = pointerPoint.Position.X;
        if (!_isDragging)
        {
            if (Math.Abs(x - _pressX) < DragThreshold)
            {
                return;
            }

            _isDragging = true;
            _insertionMarker.Visibility = Visibility.Visible;
        }

        UpdateInsertionSlot(x);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var tab = _pressedTab;
        var wasDragging = _isDragging;
        var slot = _insertionSlot;

        EndTracking();

        if (!wasDragging
            || tab is null
            || ViewModel is null)
        {
            return;
        }

        var currentIndex = ViewModel.Tabs.IndexOf(tab);
        if (currentIndex >= 0)
        {
            ViewModel.MoveTab(tab, GetTargetIndex(currentIndex, slot));
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // The button loses capture when a drag begins, so only a press with no drag ends here.
        if (!_isDragging)
        {
            EndTracking();
        }
    }

    // A tab's slot is the count of tabs with their middle left of the pointer.
    private void UpdateInsertionSlot(double pointerX)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        var slot = 0;
        var markerX = 0.0;

        foreach (var tab in viewModel.Tabs)
        {
            if (_tabItems.ContainerFromItem(tab) is not FrameworkElement container)
            {
                continue;
            }

            var left = container.TransformToVisual(_overlay).TransformPoint(default).X;
            var right = left + container.ActualWidth;

            if (pointerX >= (left + right) / 2)
            {
                slot++;
                markerX = right;
            }
            else
            {
                if (slot == 0)
                {
                    markerX = left;
                }

                break;
            }
        }

        _insertionSlot = slot;
        Canvas.SetLeft(_insertionMarker, Math.Max(0, markerX - (_insertionMarker.Width / 2)));
    }

    private void EndTracking()
    {
        _pressedTab = null;
        _isDragging = false;
        _insertionMarker.Visibility = Visibility.Collapsed;

        if (_inputRoot is null)
        {
            return;
        }

        _inputRoot.RemoveHandler(UIElement.PointerMovedEvent, _pointerMovedHandler);
        _inputRoot.RemoveHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler);
        _inputRoot.RemoveHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
        _inputRoot = null;
    }
}
