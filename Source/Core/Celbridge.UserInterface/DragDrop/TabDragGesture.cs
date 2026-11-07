using Microsoft.UI.Dispatching;
using Windows.Foundation;

using KeyEventHandler = Microsoft.UI.Xaml.Input.KeyEventHandler;

namespace Celbridge.UserInterface.DragDrop;

/// <summary>
/// The pointer drag that moves a tab in a TabStripView. Once the pointer moves past the drag threshold, the tab dims and
/// a ghost follows the pointer. The owner resolves and draws the drop slot under the pointer, and the release moves the
/// tab there. Escape cancels the drag, and holding the pointer near either end of a strip scrolls that strip.
/// </summary>
public sealed class TabDragGesture
{
    private const double DragThreshold = 5.0;
    private const double DraggedTabOpacity = 0.5;
    private const double AutoScrollEdgeWidth = 28.0;
    private const double AutoScrollStep = 16.0;

    private readonly ITabDragOwner _owner;
    private readonly Canvas _overlay;
    private readonly DragGhost _ghost;
    private readonly PointerEventHandler _pointerMovedHandler;
    private readonly PointerEventHandler _pointerReleasedHandler;
    private readonly PointerEventHandler _pointerCaptureLostHandler;
    private readonly KeyEventHandler _keyDownHandler;
    private readonly DispatcherQueueTimer _autoScrollTimer;

    private TabViewItem? _pressedTab;
    private UIElement? _inputRoot;
    private Point _pressPosition;
    private Point _lastPointerPosition;
    private bool _isDragging;
    private double _autoScrollDelta;
    private TabDropSlot? _currentSlot;

    /// <summary>
    /// Creates the gesture. The ghost is drawn in the given overlay, and positions are reported relative to it.
    /// </summary>
    public TabDragGesture(ITabDragOwner owner, Canvas overlay)
    {
        _owner = owner;
        _overlay = overlay;
        _ghost = new DragGhost(overlay);
        _pointerMovedHandler = OnPointerMoved;
        _pointerReleasedHandler = OnPointerReleased;
        _pointerCaptureLostHandler = OnPointerCaptureLost;
        _keyDownHandler = OnRootKeyDown;

        _autoScrollTimer = overlay.DispatcherQueue.CreateTimer();
        _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(50);
        _autoScrollTimer.Tick += OnAutoScrollTick;
    }

    /// <summary>
    /// Returns true from the moment the pointer passes the drag threshold until the drag ends.
    /// </summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    /// Returns the insertion slot for a pointer at the given X. The slot is the index of the first laid-out tab with its
    /// center past the pointer, or else the index after the last laid-out tab. A tab not laid out has empty bounds.
    /// </summary>
    public static int GetInsertionSlot(double pointerX, IReadOnlyList<Rect> tabBounds)
    {
        var slot = 0;
        for (int i = 0; i < tabBounds.Count; i++)
        {
            var bounds = tabBounds[i];
            if (bounds.IsEmpty)
            {
                continue;
            }

            if (pointerX < bounds.X + (bounds.Width / 2))
            {
                return i;
            }

            slot = i + 1;
        }

        return slot;
    }

    /// <summary>
    /// Returns the new index of a tab dropped in a slot of the strip holding the tab. Slot N sits before the tab at index
    /// N. Removing the dragged tab shifts every later slot down by one.
    /// </summary>
    public static int GetTargetIndex(int currentIndex, int insertionSlot)
    {
        return insertionSlot > currentIndex ? insertionSlot - 1 : insertionSlot;
    }

    /// <summary>
    /// Begins tracking a press on a tab. The drag starts only once the pointer passes the drag threshold, so a plain
    /// click is unaffected. A press on a button inside the tab, like the close button, starts no drag.
    /// </summary>
    public void OnTabPressed(TabViewItem tab, PointerRoutedEventArgs e)
    {
        if (_pressedTab is not null)
        {
            // A stale press, with the release delivered elsewhere.
            EndTracking();
        }

        if (!e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed
            || IsPressOnButton(e.OriginalSource, tab))
        {
            return;
        }

        // The tab list captures the pointer on press, which routes the following events to the capture owner and up
        // its ancestors. The window's content is on that path, and the tab is not.
        if (_overlay.XamlRoot?.Content is not UIElement inputRoot)
        {
            return;
        }

        _pressedTab = tab;
        _inputRoot = inputRoot;
        _pressPosition = e.GetCurrentPoint(_overlay).Position;

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
        var position = pointerPoint.Position;

        if (!_isDragging)
        {
            // A move with the button up means the release went elsewhere, for example into a web view. The release
            // can also be the second release of a double tap. Dropping the press stops a drag from following a pointer
            // with the button up.
            if (!pointerPoint.Properties.IsLeftButtonPressed)
            {
                EndTracking();
                return;
            }

            if (Math.Abs(position.X - _pressPosition.X) < DragThreshold
                && Math.Abs(position.Y - _pressPosition.Y) < DragThreshold)
            {
                return;
            }

            BeginDrag(e);
        }

        UpdateDrag(position);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var tab = _pressedTab;
        var slot = _currentSlot;
        bool wasDragging = _isDragging;

        EndTracking();

        if (!wasDragging
            || tab is null)
        {
            // A plain click. The tab's own handling selects the tab.
            return;
        }

        e.Handled = true;

        if (slot is not null)
        {
            _owner.CompleteDrag(tab, slot);
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Taking the capture when the drag begins raises PointerCaptureLost on the strip's list. Only the dragged tab
        // losing its own capture cancels the drag.
        if (_isDragging
            && ReferenceEquals(e.OriginalSource, _pressedTab))
        {
            EndTracking();
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape
            && _pressedTab is not null)
        {
            e.Handled = true;
            EndTracking();
        }
    }

    private void BeginDrag(PointerRoutedEventArgs e)
    {
        var tab = _pressedTab!;

        _isDragging = true;
        tab.CapturePointer(e.Pointer);
        tab.Opacity = DraggedTabOpacity;
        _ghost.Show(_owner.GetDragLabel(tab));

        // Escape cancels the drag. Wired on the window content, so it fires wherever managed focus sits. A keystroke
        // inside a native web view reaches the managed layer only once the web view forwards it.
        _inputRoot?.AddHandler(UIElement.KeyDownEvent, _keyDownHandler, handledEventsToo: true);
    }

    private void UpdateDrag(Point position)
    {
        var tab = _pressedTab!;

        _lastPointerPosition = position;
        _currentSlot = _owner.FindDropSlot(tab, position);

        // The ghost is display only, so it draws over the native web views on every head.
        _ghost.CenterOn(position);
        _owner.ShowDropSlot(tab, _currentSlot);

        UpdateAutoScroll(position);
    }

    private void UpdateAutoScroll(Point position)
    {
        double delta = 0;
        if (_currentSlot is { OverStrip: true } slot)
        {
            var stripBounds = slot.Strip.GetStripBounds(_overlay);
            if (position.X < stripBounds.X + AutoScrollEdgeWidth)
            {
                delta = -AutoScrollStep;
            }
            else if (position.X > stripBounds.Right - AutoScrollEdgeWidth)
            {
                delta = AutoScrollStep;
            }
        }

        _autoScrollDelta = delta;
        if (delta == 0)
        {
            _autoScrollTimer.Stop();
        }
        else if (!_autoScrollTimer.IsRunning)
        {
            _autoScrollTimer.Start();
        }
    }

    private void OnAutoScrollTick(DispatcherQueueTimer sender, object args)
    {
        if (!_isDragging
            || _autoScrollDelta == 0
            || _currentSlot is not { OverStrip: true } slot)
        {
            sender.Stop();
            return;
        }

        slot.Strip.ScrollBy(_autoScrollDelta);

        // Tabs slide under the still pointer, so the slot and its feedback are resolved again.
        UpdateDrag(_lastPointerPosition);
    }

    private void EndTracking()
    {
        var tab = _pressedTab;

        if (_inputRoot is not null)
        {
            _inputRoot.RemoveHandler(UIElement.PointerMovedEvent, _pointerMovedHandler);
            _inputRoot.RemoveHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler);
            _inputRoot.RemoveHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
            _inputRoot.RemoveHandler(UIElement.KeyDownEvent, _keyDownHandler);
            _inputRoot = null;
        }

        _autoScrollTimer.Stop();
        _ghost.Hide();

        if (tab is not null)
        {
            tab.ReleasePointerCaptures();
            tab.Opacity = 1.0;

            if (_isDragging)
            {
                _owner.ShowDropSlot(tab, null);
            }
        }

        _pressedTab = null;
        _isDragging = false;
        _autoScrollDelta = 0;
        _currentSlot = null;
    }

    private static bool IsPressOnButton(object? originalSource, TabViewItem tab)
    {
        var current = originalSource as DependencyObject;
        while (current is not null
            && !ReferenceEquals(current, tab))
        {
            if (current is ButtonBase)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
