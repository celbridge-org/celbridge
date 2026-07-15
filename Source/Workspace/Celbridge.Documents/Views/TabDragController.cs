using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

using KeyEventHandler = Microsoft.UI.Xaml.Input.KeyEventHandler;

namespace Celbridge.Documents.Views;

/// <summary>
/// The drop target a tab drag is currently over: the section that will receive the tab, the
/// insertion slot within its tab order (computed from the pointer X whether over the strip or the
/// body), and whether the pointer is over the section body rather than the tab strip.
/// </summary>
internal record TabDropTarget(DocumentSection Section, int InsertionSlot, bool OverSectionBody);

/// <summary>
/// Pointer-driven drag controller for document tabs, replacing the built-in TabView drag-and-drop
/// on heads where it is unreliable. Tracks a pressed tab through a drag threshold, renders a ghost
/// and insertion indicator in an overlay canvas, and commits the reorder or move on release.
/// </summary>
internal sealed class TabDragController
{
    private const double DragThreshold = 5.0;
    private const double GhostOpacity = 0.85;
    private const double IndicatorWidth = 2.0;
    private const double AutoScrollEdgeWidth = 28.0;
    private const double AutoScrollStep = 16.0;

    private readonly DocumentSectionContainer _container;
    private readonly Canvas _overlay;
    private readonly PointerEventHandler _pointerMovedHandler;
    private readonly PointerEventHandler _pointerReleasedHandler;
    private readonly PointerEventHandler _pointerCaptureLostHandler;
    private readonly KeyEventHandler _keyDownHandler;
    private readonly DispatcherQueueTimer _autoScrollTimer;

    private DocumentTab? _pressedTab;
    private DocumentSection? _sourceSection;
    private Point _pressPosition;
    private Point _lastPointerPosition;
    private bool _isDragging;
    private double _autoScrollDelta;
    private UIElement? _keyEventRoot;
    private TabDropTarget? _currentTarget;
    private Border? _ghost;
    private Border? _insertionIndicator;
    private Border? _sectionHighlight;

    public TabDragController(DocumentSectionContainer container, Canvas overlay)
    {
        _container = container;
        _overlay = overlay;
        _pointerMovedHandler = OnPointerMoved;
        _pointerReleasedHandler = OnPointerReleased;
        _pointerCaptureLostHandler = OnPointerCaptureLost;
        _keyDownHandler = OnRootKeyDown;

        _autoScrollTimer = container.DispatcherQueue.CreateTimer();
        _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(50);
        _autoScrollTimer.Tick += OnAutoScrollTick;
    }

    /// <summary>
    /// Begins tracking a pointer press on a tab header. The drag itself starts only if the pointer
    /// travels past the drag threshold, so plain clicks are unaffected.
    /// </summary>
    public void OnTabPressed(DocumentSection section, DocumentTab tab, PointerRoutedEventArgs e)
    {
        if (_pressedTab is not null)
        {
            // A stale press whose release was delivered elsewhere - reset before tracking anew.
            EndTracking();
        }

        var pointerPoint = e.GetCurrentPoint(tab);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Presses on interactive children (the tab close button) never start a drag.
        if (IsPressOnInteractiveChild(e.OriginalSource, tab))
        {
            return;
        }

        _pressedTab = tab;
        _sourceSection = section;
        _pressPosition = e.GetCurrentPoint(_overlay).Position;

        // Track the pointer on the container, not the tab: the tab strip's list captures the pointer
        // on press, which routes subsequent events to the capture owner and up its ancestor chain.
        // The container is on that chain; the tab is not.
        _container.AddHandler(UIElement.PointerMovedEvent, _pointerMovedHandler, handledEventsToo: true);
        _container.AddHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler, handledEventsToo: true);
        _container.AddHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler, handledEventsToo: true);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedTab is null)
        {
            return;
        }

        var position = e.GetCurrentPoint(_overlay).Position;

        if (!_isDragging)
        {
            if (Math.Abs(position.X - _pressPosition.X) < DragThreshold &&
                Math.Abs(position.Y - _pressPosition.Y) < DragThreshold)
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
        if (_pressedTab is null)
        {
            return;
        }

        var tab = _pressedTab;
        var sourceSection = _sourceSection!;
        var target = _currentTarget;
        bool wasDragging = _isDragging;

        EndTracking();

        if (!wasDragging)
        {
            // A plain click - leave selection and activation to the normal tap handling.
            return;
        }

        e.Handled = true;

        if (target is not null)
        {
            _container.CommitTabDrag(tab, sourceSection, target.Section, target.InsertionSlot);
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedTab is null)
        {
            return;
        }

        // Stealing capture in BeginDrag raises PointerCaptureLost on the strip's list; only the
        // dragged tab losing its own capture cancels the drag.
        if (_isDragging &&
            ReferenceEquals(e.OriginalSource, _pressedTab))
        {
            EndTracking();
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape &&
            _pressedTab is not null)
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
        tab.Opacity = 0.5;
        CreateDragVisuals(tab);

        // Escape cancels the drag. Wired on the window content so it fires wherever managed focus
        // sits; keystrokes inside a native web view do not reach the managed layer.
        _keyEventRoot = _container.XamlRoot?.Content as UIElement;
        _keyEventRoot?.AddHandler(UIElement.KeyDownEvent, _keyDownHandler, handledEventsToo: true);
    }

    private void UpdateDrag(Point position)
    {
        var tab = _pressedTab!;
        _lastPointerPosition = position;
        _currentTarget = ResolveDropTarget(position);

        // The ghost follows the pointer. Display-only drag visuals are not occluded by the native web
        // views on this head, so it stays visible over editor content.
        if (_ghost is not null)
        {
            double ghostX = Math.Clamp(position.X - (_ghost.ActualWidth / 2), 0, Math.Max(0, _overlay.ActualWidth - _ghost.ActualWidth));
            double ghostY = Math.Clamp(position.Y - (_ghost.ActualHeight / 2), 0, Math.Max(0, _overlay.ActualHeight - _ghost.ActualHeight));
            Canvas.SetLeft(_ghost, ghostX);
            Canvas.SetTop(_ghost, ghostY);
        }

        UpdateInsertionIndicator(tab);
        UpdateSectionHighlight();
        UpdateAutoScroll(position);
    }

    /// <summary>
    /// Classifies the pointer position against the visible sections. The insertion slot is the pointer
    /// X against the tab centres in both cases, so the indicator tracks the pointer over the body just
    /// as it does over the strip; the body hit only differs in showing the section highlight.
    /// </summary>
    private TabDropTarget? ResolveDropTarget(Point position)
    {
        for (int i = 0; i < _container.SectionCount; i++)
        {
            var section = _container.GetSection(i);

            var stripBounds = section.GetTabStripBounds(_overlay);
            if (!stripBounds.IsEmpty &&
                stripBounds.Contains(position))
            {
                int insertionSlot = ComputeInsertionSlot(section, position.X);
                return new TabDropTarget(section, insertionSlot, OverSectionBody: false);
            }

            var sectionBounds = GetElementBounds(section);
            if (sectionBounds.Contains(position))
            {
                int insertionSlot = ComputeInsertionSlot(section, position.X);
                return new TabDropTarget(section, insertionSlot, OverSectionBody: true);
            }
        }

        return null;
    }

    private int ComputeInsertionSlot(DocumentSection section, double pointerX)
    {
        var headerBounds = section.GetTabHeaderBounds(_overlay);
        for (int i = 0; i < headerBounds.Count; i++)
        {
            var bounds = headerBounds[i].Bounds;
            double centerX = bounds.X + (bounds.Width / 2);
            if (pointerX < centerX)
            {
                return i;
            }
        }

        return headerBounds.Count;
    }

    private void UpdateInsertionIndicator(DocumentTab draggedTab)
    {
        if (_insertionIndicator is null)
        {
            return;
        }

        var target = _currentTarget;
        if (target is null)
        {
            _insertionIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var stripBounds = target.Section.GetTabStripBounds(_overlay);
        if (stripBounds.IsEmpty)
        {
            _insertionIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var headerBounds = target.Section.GetTabHeaderBounds(_overlay);
        int slot = target.InsertionSlot;
        int draggedIndex = target.Section.GetTabIndex(draggedTab);

        // Position the indicator at the slot boundary: the left edge of the slot's tab, the right
        // edge of the last tab for the trailing slot (append), or the strip start when empty. A slot
        // adjacent to the dragged tab's own position is a no-op drop, so mark the tab's left edge -
        // that is where it stays.
        bool dropsInPlace = draggedIndex >= 0 &&
            (slot == draggedIndex || slot == draggedIndex + 1);
        double indicatorX;
        if (dropsInPlace &&
            draggedIndex < headerBounds.Count)
        {
            indicatorX = headerBounds[draggedIndex].Bounds.X;
        }
        else if (headerBounds.Count == 0)
        {
            indicatorX = stripBounds.X;
        }
        else if (slot < headerBounds.Count)
        {
            indicatorX = headerBounds[slot].Bounds.X;
        }
        else
        {
            var lastBounds = headerBounds[^1].Bounds;
            indicatorX = lastBounds.X + lastBounds.Width;
        }

        _insertionIndicator.Height = stripBounds.Height;
        Canvas.SetLeft(_insertionIndicator, indicatorX - (IndicatorWidth / 2));
        Canvas.SetTop(_insertionIndicator, stripBounds.Y);
        _insertionIndicator.Visibility = Visibility.Visible;
    }

    private void UpdateSectionHighlight()
    {
        if (_sectionHighlight is null)
        {
            return;
        }

        if (_currentTarget is not { OverSectionBody: true } target)
        {
            _sectionHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        // Cover only the content area below the tab strip band. The ghost tab rides in that band, so
        // highlighting it too would read as a phantom strip - most visibly on an empty section, where
        // the ghost is the only thing in an otherwise bare strip.
        var sectionBounds = GetElementBounds(target.Section);
        var stripBand = GetDragStripBounds();
        double top = stripBand.IsEmpty ? sectionBounds.Y : stripBand.Bottom;

        _sectionHighlight.Width = sectionBounds.Width;
        _sectionHighlight.Height = Math.Max(0, sectionBounds.Bottom - top);
        Canvas.SetLeft(_sectionHighlight, sectionBounds.X);
        Canvas.SetTop(_sectionHighlight, top);
        _sectionHighlight.Visibility = Visibility.Visible;
    }

    private void UpdateAutoScroll(Point position)
    {
        double delta = 0;
        if (_currentTarget is { OverSectionBody: false } target)
        {
            var stripBounds = target.Section.GetTabStripBounds(_overlay);
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
        if (delta != 0)
        {
            if (!_autoScrollTimer.IsRunning)
            {
                _autoScrollTimer.Start();
            }
        }
        else
        {
            _autoScrollTimer.Stop();
        }
    }

    private void OnAutoScrollTick(DispatcherQueueTimer sender, object args)
    {
        if (!_isDragging ||
            _autoScrollDelta == 0 ||
            _currentTarget is not { OverSectionBody: false } target)
        {
            sender.Stop();
            return;
        }

        target.Section.ScrollTabStripBy(_autoScrollDelta);

        // Tabs slide under the stationary pointer, so recompute the slot and indicator.
        UpdateDrag(_lastPointerPosition);
    }

    private void CreateDragVisuals(DocumentTab tab)
    {
        var ghostLabel = new TextBlock
        {
            Text = tab.ViewModel.DocumentName,
            Foreground = GetThemeBrush("TextFillColorPrimaryBrush", Microsoft.UI.Colors.Black),
            VerticalAlignment = VerticalAlignment.Center
        };

        _ghost = new Border
        {
            Background = GetThemeBrush("LayerFillColorDefaultBrush", Microsoft.UI.Colors.LightGray),
            BorderBrush = GetThemeBrush("ControlStrokeColorDefaultBrush", Microsoft.UI.Colors.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 4, 10, 4),
            Opacity = GhostOpacity,
            IsHitTestVisible = false,
            Child = ghostLabel
        };

        _insertionIndicator = new Border
        {
            Width = IndicatorWidth,
            Background = GetThemeBrush("AccentFillColorDefaultBrush", Microsoft.UI.Colors.DodgerBlue),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        _sectionHighlight = new Border
        {
            Background = CreateAccentHighlightBrush(),
            BorderBrush = GetThemeBrush("AccentFillColorDefaultBrush", Microsoft.UI.Colors.DodgerBlue),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        _overlay.Children.Add(_sectionHighlight);
        _overlay.Children.Add(_insertionIndicator);
        _overlay.Children.Add(_ghost);
    }

    private void EndTracking()
    {
        var tab = _pressedTab!;

        _container.RemoveHandler(UIElement.PointerMovedEvent, _pointerMovedHandler);
        _container.RemoveHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler);
        _container.RemoveHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
        _keyEventRoot?.RemoveHandler(UIElement.KeyDownEvent, _keyDownHandler);
        _keyEventRoot = null;

        _autoScrollTimer.Stop();
        tab.ReleasePointerCaptures();
        tab.Opacity = 1.0;

        if (_ghost is not null)
        {
            _overlay.Children.Remove(_ghost);
            _ghost = null;
        }
        if (_insertionIndicator is not null)
        {
            _overlay.Children.Remove(_insertionIndicator);
            _insertionIndicator = null;
        }
        if (_sectionHighlight is not null)
        {
            _overlay.Children.Remove(_sectionHighlight);
            _sectionHighlight = null;
        }

        _pressedTab = null;
        _sourceSection = null;
        _isDragging = false;
        _autoScrollDelta = 0;
        _currentTarget = null;
    }

    /// <summary>
    /// Gets the tab strip band relevant to the current drag: the target section's strip when over
    /// one, otherwise the source section's. The ghost rides this band and the section highlight
    /// starts just below it.
    /// </summary>
    private Rect GetDragStripBounds()
    {
        if (_currentTarget is not null)
        {
            var targetBounds = _currentTarget.Section.GetTabStripBounds(_overlay);
            if (!targetBounds.IsEmpty)
            {
                return targetBounds;
            }
        }

        return _sourceSection?.GetTabStripBounds(_overlay) ?? Rect.Empty;
    }

    private Rect GetElementBounds(UIElement element)
    {
        if (element is FrameworkElement frameworkElement &&
            frameworkElement.ActualWidth > 0)
        {
            var transform = element.TransformToVisual(_overlay);
            return transform.TransformBounds(new Rect(0, 0, frameworkElement.ActualWidth, frameworkElement.ActualHeight));
        }

        return Rect.Empty;
    }

    private static bool IsPressOnInteractiveChild(object? originalSource, DocumentTab tab)
    {
        var current = originalSource as DependencyObject;
        while (current is not null && current != tab)
        {
            if (current is ButtonBase)
            {
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static Brush GetThemeBrush(string resourceKey, Windows.UI.Color fallbackColor)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallbackColor);
    }

    private static Windows.UI.Color GetThemeColor(string resourceKey, Windows.UI.Color fallbackColor)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return fallbackColor;
    }

    private static Brush CreateAccentHighlightBrush()
    {
        var color = GetThemeColor("AccentFillColorDefaultBrush", Microsoft.UI.Colors.DodgerBlue);
        color.A = 0x28;

        return new SolidColorBrush(color);
    }
}
