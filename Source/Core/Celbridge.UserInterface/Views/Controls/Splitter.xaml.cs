using Celbridge.UserInterface.Services;
using Microsoft.UI.Input;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A reusable splitter control for resizing adjacent elements in a Grid.
/// Supports both vertical (column) and horizontal (row) orientations.
/// </summary>
public sealed partial class Splitter : UserControl
{
    /// <summary>
    /// The orientation of the splitter (Vertical resizes columns, Horizontal resizes rows).
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(
            nameof(Orientation),
            typeof(Orientation),
            typeof(Splitter),
            new PropertyMetadata(Orientation.Vertical, OnOrientationChanged));

    /// <summary>
    /// The brush filling the grab area at rest, which also gives it a leading edge. Null by default, because
    /// a splitter between two panels sits in a gutter whose own fill carries the boundary. A splitter
    /// dividing two panes of one document has no gutter behind it and takes the chrome brush, so its grab
    /// area carries the boundary itself.
    /// </summary>
    public Brush? GrabAreaBrush
    {
        get => (Brush?)GetValue(GrabAreaBrushProperty);
        set => SetValue(GrabAreaBrushProperty, value);
    }

    public static readonly DependencyProperty GrabAreaBrushProperty =
        DependencyProperty.Register(
            nameof(GrabAreaBrush),
            typeof(Brush),
            typeof(Splitter),
            new PropertyMetadata(null, OnGrabAreaBrushChanged));

    /// <summary>
    /// The thickness of the splitter line while dragging.
    /// </summary>
    public double DraggingLineThickness
    {
        get => (double)GetValue(DraggingLineThicknessProperty);
        set => SetValue(DraggingLineThicknessProperty, value);
    }

    public static readonly DependencyProperty DraggingLineThicknessProperty =
        DependencyProperty.Register(
            nameof(DraggingLineThickness),
            typeof(double),
            typeof(Splitter),
            new PropertyMetadata(4.0));

    /// <summary>
    /// The width of the interactive (grabbable) area in pixels. This is also the visible width of the
    /// gutter between two panels, since the grab area is what holds that gap open. Resolved from the
    /// GutterSize resource when the splitter loads unless a caller sets it explicitly.
    /// </summary>
    public double GrabAreaSize
    {
        get => (double)GetValue(GrabAreaSizeProperty);
        set => SetValue(GrabAreaSizeProperty, value);
    }

    public static readonly DependencyProperty GrabAreaSizeProperty =
        DependencyProperty.Register(
            nameof(GrabAreaSize),
            typeof(double),
            typeof(Splitter),
            new PropertyMetadata(0.0, OnGrabAreaSizeChanged));

    /// <summary>
    /// Event raised when a drag operation starts.
    /// </summary>
    public event EventHandler? DragStarted;

    /// <summary>
    /// Event raised when a drag operation completes.
    /// </summary>
    public event EventHandler? DragCompleted;

    /// <summary>
    /// Event raised during drag with the delta position.
    /// </summary>
    public event EventHandler<double>? DragDelta;

    /// <summary>
    /// Event raised when the splitter is double-clicked.
    /// </summary>
    public event EventHandler? DoubleClicked;

    private const int NormalZIndex = 100;
    private const int DraggingZIndex = 200;
    private const int DoubleClickDebounceMs = 500;
    private const double GrabAreaEdgeThickness = 1.0;

    private readonly SolidColorBrush _transparentGrabAreaBrush = new(Colors.Transparent);

    private bool _isDragging;
    private double _dragStartPosition;
    private Brush? _normalBrush;
    private Brush? _draggingBrush;
    private DateTime _lastDoubleClickTime;
    private ISplitterCursorController? _cursorController;

    public Splitter()
    {
        InitializeComponent();

        // Ensure the splitter renders above the adjacent panel content its grab area overlaps.
        Canvas.SetZIndex(this, NormalZIndex);

        // Set up pointer event handlers
        SplitterBorder.PointerEntered += OnPointerEntered;
        SplitterBorder.PointerExited += OnPointerExited;
        SplitterBorder.PointerPressed += OnPointerPressed;
        SplitterBorder.PointerMoved += OnPointerMoved;
        SplitterBorder.PointerReleased += OnPointerReleased;
        SplitterBorder.PointerCaptureLost += OnPointerCaptureLost;
        SplitterBorder.DoubleTapped += OnDoubleTapped;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _cursorController = ServiceLocator.AcquireService<ISplitterCursorController>();

        _normalBrush = (Brush)Application.Current.Resources["DividerBrush"];
        _draggingBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];

        // The grab area is the gutter, so it takes the shared size unless this splitter was given one.
        if (ReadLocalValue(GrabAreaSizeProperty) == DependencyProperty.UnsetValue)
        {
            GrabAreaSize = (double)Application.Current.Resources["GutterSize"];
        }

        // Apply initial orientation
        UpdateOrientation();
        UpdateLineThickness();
        UpdateGrabAreaSize();
        UpdateGrabAreaBrush();

        // Defensive reset: cancel any fade-in that a transient pointer enter raised during
        // construction so the hover line starts hidden rather than stuck on.
        HoverFadeIn.Stop();
        HoverLine.Opacity = 0;
    }

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Splitter splitter)
        {
            splitter.UpdateOrientation();
        }
    }

    private static void OnGrabAreaBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Splitter splitter)
        {
            splitter.UpdateGrabAreaBrush();
        }
    }

    private static void OnGrabAreaSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Splitter splitter)
        {
            splitter.UpdateGrabAreaSize();
        }
    }

    // A splitter fills the gutter it sits in, so its lines run down the middle of that gutter.
    private void UpdateOrientation()
    {
        if (Orientation == Orientation.Vertical)
        {
            // Vertical splitter (resizes columns left/right).
            SplitterLine.HorizontalAlignment = HorizontalAlignment.Center;
            SplitterLine.VerticalAlignment = VerticalAlignment.Stretch;
            HoverLine.HorizontalAlignment = HorizontalAlignment.Center;
            HoverLine.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            // Horizontal splitter (resizes rows top/bottom).
            SplitterLine.HorizontalAlignment = HorizontalAlignment.Stretch;
            SplitterLine.VerticalAlignment = VerticalAlignment.Center;
            HoverLine.HorizontalAlignment = HorizontalAlignment.Stretch;
            HoverLine.VerticalAlignment = VerticalAlignment.Center;
        }

        UpdateLineThickness();
        UpdateGrabAreaSize();
        UpdateGrabAreaBrush();
        ApplyManagedCursor();
    }

    // The line is hidden at rest and only appears under the pointer or a drag: the fill either side of the
    // splitter carries the boundary, whether that is the gutter it sits in or its own grab area.
    private void UpdateLineThickness()
    {
        if (Orientation == Orientation.Vertical)
        {
            SplitterLine.Width = 0;
            SplitterLine.Height = double.NaN; // Stretch
            HoverLine.Width = DraggingLineThickness;
            HoverLine.Height = double.NaN; // Stretch
        }
        else
        {
            SplitterLine.Width = double.NaN; // Stretch
            SplitterLine.Height = 0;
            HoverLine.Width = double.NaN; // Stretch
            HoverLine.Height = DraggingLineThickness;
        }
    }

    private void UpdateGrabAreaBrush()
    {
        if (GrabAreaBrush is null)
        {
            // Transparent rather than null, so an unfilled grab area is still hit testable for the drag. It
            // sits in a gutter, and the panels either side of it draw the edges, so it draws none of its own.
            SplitterBorder.Background = _transparentGrabAreaBrush;
            SplitterBorder.BorderThickness = new Thickness(0);
            return;
        }

        SplitterBorder.Background = GrabAreaBrush;

        // One edge, on the leading side. That side faces the document's main pane, whose content the host
        // does not control. The trailing side faces a panel of ours that the fill already reads against.
        if (Orientation == Orientation.Vertical)
        {
            SplitterBorder.BorderThickness = new Thickness(GrabAreaEdgeThickness, 0, 0, 0);
            return;
        }

        SplitterBorder.BorderThickness = new Thickness(0, GrabAreaEdgeThickness, 0, 0);
    }

    private void UpdateGrabAreaSize()
    {
        // The control is docked to the boundary by its alignment (or fills its own gutter column), so a zero
        // margin keeps the whole grab area inside its own panel, clear of the adjacent editor.
        if (Orientation == Orientation.Vertical)
        {
            Width = GrabAreaSize;
            Height = double.NaN; // Stretch
        }
        else
        {
            Width = double.NaN; // Stretch
            Height = GrabAreaSize;
        }

        Margin = new Thickness(0);
    }

    private void BeginHoverFadeIn()
    {
        // Stop the fade-out so the two opacity animations never run together.
        HoverFadeOut.Stop();
        HoverFadeIn.Begin();

        // Drive the OS cursor from the same enter and exit signals as the hover highlight so the two always
        // agree. The managed cursor is unreliable on the Skia head, so the native controller keeps the resize
        // cursor correct there.
        _cursorController?.SetCursor(ResizeCursorShape);
    }

    private void BeginHoverFadeOut()
    {
        // Capture the current (animated) opacity before stopping the fade-in. Stop reverts the
        // opacity to its base value of 0, so without an explicit From the fade-out would have
        // nothing to animate down from and would snap off in a single frame.
        var currentOpacity = HoverLine.Opacity;

        // Cancel any pending or in-progress fade-in first. The fade-in has a start delay, so
        // without this a fade-in scheduled just before the pointer left fires afterwards and
        // leaves the hover line stuck on. This is what made splitters appear highlighted
        // without being hovered, since the Skia head raises transient pointer enter/exit
        // pairs during layout that trip exactly this race.
        HoverFadeIn.Stop();

        // Animate down from the captured opacity so the fade-out is smooth.
        HoverFadeOutAnimation.From = currentOpacity;
        HoverFadeOut.Begin();

        _cursorController?.SetCursor(SplitterCursorShape.Default);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        BeginHoverFadeIn();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            BeginHoverFadeOut();
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Prevent drag operations immediately after a double-click
        if ((DateTime.UtcNow - _lastDoubleClickTime).TotalMilliseconds < DoubleClickDebounceMs)
        {
            e.Handled = true;
            return;
        }

        _isDragging = true;

        // Hold the resize cursor for the drag. The hover highlight stays lit while dragging, so the cursor
        // matches it.
        _cursorController?.SetCursor(ResizeCursorShape);

        var point = e.GetCurrentPoint(this.Parent as UIElement);
        _dragStartPosition = Orientation == Orientation.Vertical
            ? point.Position.X
            : point.Position.Y;

        // Raise z-index so this splitter appears above others while dragging
        Canvas.SetZIndex(this, DraggingZIndex);

        SplitterBorder.CapturePointer(e.Pointer);

        // Change to accent color and expand width while dragging
        if (_draggingBrush != null)
        {
            SplitterLine.Fill = _draggingBrush;
        }

        // Expand the line to the dragging thickness while dragging
        if (Orientation == Orientation.Vertical)
        {
            SplitterLine.Width = DraggingLineThickness;
        }
        else
        {
            SplitterLine.Height = DraggingLineThickness;
        }

        // Notify that drag has started
        DragStarted?.Invoke(this, EventArgs.Empty);

        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        // Re-assert the resize cursor on every drag move. A fast drag sweeps the captured pointer over the
        // native editor web view, whose tracking area sets an arrow cursor. Re-asserting here overrides it so
        // the resize cursor holds for the whole drag.
        _cursorController?.SetCursor(ResizeCursorShape);

        // Skip DragDelta events within debounce window after a double-click
        // This prevents cached sizes in SplitterHelper from overwriting reset values
        if ((DateTime.UtcNow - _lastDoubleClickTime).TotalMilliseconds < DoubleClickDebounceMs)
        {
            e.Handled = true;
            return;
        }

        var point = e.GetCurrentPoint(this.Parent as UIElement);
        var currentPosition = Orientation == Orientation.Vertical
            ? point.Position.X
            : point.Position.Y;

        var delta = currentPosition - _dragStartPosition;

        DragDelta?.Invoke(this, delta);

        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            SplitterBorder.ReleasePointerCapture(e.Pointer);

            // Restore normal z-index
            Canvas.SetZIndex(this, NormalZIndex);

            // Restore normal color and size
            if (_normalBrush != null)
            {
                SplitterLine.Fill = _normalBrush;
            }

            // Restore original line thickness
            UpdateLineThickness();

            BeginHoverFadeOut();

            DragCompleted?.Invoke(this, EventArgs.Empty);

            e.Handled = true;
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;

            // Restore normal z-index
            Canvas.SetZIndex(this, NormalZIndex);

            // Restore normal color and size
            if (_normalBrush != null)
            {
                SplitterLine.Fill = _normalBrush;
            }

            // Restore original line thickness
            UpdateLineThickness();

            BeginHoverFadeOut();

            DragCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private SplitterCursorShape ResizeCursorShape => Orientation == Orientation.Vertical
        ? SplitterCursorShape.ResizeColumns
        : SplitterCursorShape.ResizeRows;

    // Sets the managed resize cursor for the element. This shows the resize cursor on the packaged Windows
    // head. The Skia heads drive the cursor natively instead, where the managed cursor is unreliable.
    private void ApplyManagedCursor()
    {
        var cursorShape = Orientation == Orientation.Vertical
            ? InputSystemCursorShape.SizeWestEast
            : InputSystemCursorShape.SizeNorthSouth;

        var oldCursor = ProtectedCursor;
        ProtectedCursor = InputSystemCursor.Create(cursorShape);
        oldCursor?.Dispose();
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _lastDoubleClickTime = DateTime.UtcNow;

        BeginHoverFadeOut();

        DoubleClicked?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
