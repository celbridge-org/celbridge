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
    /// The thickness of the visible splitter line.
    /// </summary>
    public double LineThickness
    {
        get => (double)GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    public static readonly DependencyProperty LineThicknessProperty =
        DependencyProperty.Register(
            nameof(LineThickness),
            typeof(double),
            typeof(Splitter),
            new PropertyMetadata(1.0, OnLineThicknessChanged));

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
    /// The width of the interactive (grabbable) area in pixels.
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
            new PropertyMetadata(8.0, OnGrabAreaSizeChanged));

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

    private bool _isDragging;
    private double _dragStartPosition;
    private Brush? _normalBrush;
    private Brush? _draggingBrush;

    public Splitter()
    {
        InitializeComponent();

        // Ensure splitter renders above adjacent content when using negative margins for overlap.
        Canvas.SetZIndex(this, 100);

        // Set up pointer event handlers
        SplitterBorder.PointerEntered += OnPointerEntered;
        SplitterBorder.PointerExited += OnPointerExited;
        SplitterBorder.PointerPressed += OnPointerPressed;
        SplitterBorder.PointerMoved += OnPointerMoved;
        SplitterBorder.PointerReleased += OnPointerReleased;
        SplitterBorder.PointerCaptureLost += OnPointerCaptureLost;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Cache brushes
        _normalBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        _draggingBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];

        // Apply initial orientation
        UpdateOrientation();
        UpdateLineThickness();
        UpdateGrabAreaSize();
    }

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Splitter splitter)
        {
            splitter.UpdateOrientation();
        }
    }

    private static void OnLineThicknessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Splitter splitter)
        {
            splitter.UpdateLineThickness();
        }
    }

    private static void OnGrabAreaSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Splitter splitter)
        {
            splitter.UpdateGrabAreaSize();
        }
    }

    private void UpdateOrientation()
    {
        if (Orientation == Orientation.Vertical)
        {
            // Vertical splitter (resizes columns left/right)
            SplitterLine.HorizontalAlignment = HorizontalAlignment.Center;
            SplitterLine.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            // Horizontal splitter (resizes rows top/bottom)
            SplitterLine.HorizontalAlignment = HorizontalAlignment.Stretch;
            SplitterLine.VerticalAlignment = VerticalAlignment.Center;
        }

        UpdateLineThickness();
        UpdateGrabAreaSize();
    }

    private void UpdateLineThickness()
    {
        if (Orientation == Orientation.Vertical)
        {
            SplitterLine.Width = LineThickness;
            SplitterLine.Height = double.NaN; // Stretch
        }
        else
        {
            SplitterLine.Width = double.NaN; // Stretch
            SplitterLine.Height = LineThickness;
        }
    }

    private void UpdateGrabAreaSize()
    {
        if (Orientation == Orientation.Vertical)
        {
            Width = GrabAreaSize;
            Height = double.NaN; // Stretch

            // Use negative margins to overlap with adjacent content (like VS Code)
            var halfGrab = GrabAreaSize / 2;
            Margin = new Thickness(-halfGrab, 0, -halfGrab, 0);
        }
        else
        {
            Width = double.NaN; // Stretch
            Height = GrabAreaSize;
            // Use negative margins to overlap with adjacent content (like VS Code)
            var halfGrab = GrabAreaSize / 2;
            Margin = new Thickness(0, -halfGrab, 0, -halfGrab);
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        UpdateCursor();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = true;

        var point = e.GetCurrentPoint(this.Parent as UIElement);
        _dragStartPosition = Orientation == Orientation.Vertical
            ? point.Position.X
            : point.Position.Y;


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

        UpdateCursor();
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
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

            // Restore normal color and size
            if (_normalBrush != null)
            {
                SplitterLine.Fill = _normalBrush;
            }

            // Restore original line thickness
            UpdateLineThickness();

            DragCompleted?.Invoke(this, EventArgs.Empty);

            e.Handled = true;
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;

            // Restore normal color and size
            if (_normalBrush != null)
            {
                SplitterLine.Fill = _normalBrush;
            }

            // Restore original line thickness
            UpdateLineThickness();

            DragCompleted?.Invoke(this, EventArgs.Empty);
        }

        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    private void UpdateCursor()
    {
        var cursorShape = Orientation == Orientation.Vertical
            ? InputSystemCursorShape.SizeWestEast
            : InputSystemCursorShape.SizeNorthSouth;

        ProtectedCursor = InputSystemCursor.Create(cursorShape);
    }
}
