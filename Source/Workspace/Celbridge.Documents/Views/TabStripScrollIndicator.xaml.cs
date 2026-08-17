namespace Celbridge.Documents.Views;

/// <summary>
/// The thin scroll indicator drawn along the bottom of an overflowing tab strip. The thumb reports how much of
/// the strip is visible and where, and dragging it scrolls the strip.
/// </summary>
public sealed partial class TabStripScrollIndicator : UserControl
{
    // The strip geometry the thumb was last drawn from, kept so a track resize can re-place the thumb without
    // the tab strip having to measure itself again.
    private sealed record TabStripGeometry(double ContentWidth, double ViewportWidth, double Offset);

    private TabStripGeometry? _geometry;
    private double _dragStartPointerX;
    private double _dragStartThumbOffset;
    private bool _isDragging;

    /// <summary>
    /// Event raised while the thumb is dragged, carrying the strip offset the drag has reached.
    /// </summary>
    public event Action<double>? ScrollRequested;

    public TabStripScrollIndicator()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sizes and places the thumb from the strip's current geometry, collapsing the whole indicator while the
    /// strip has nothing to scroll.
    /// </summary>
    public void Update(double contentWidth, double viewportWidth, double offset)
    {
        _geometry = new TabStripGeometry(contentWidth, viewportWidth, offset);
        ApplyGeometry();
    }

    private void ApplyGeometry()
    {
        if (_geometry is null)
        {
            return;
        }

        var geometry = _geometry;
        double scrollableWidth = geometry.ContentWidth - geometry.ViewportWidth;
        if (scrollableWidth < 1 ||
            geometry.ViewportWidth <= 0)
        {
            Visibility = Visibility.Collapsed;

            return;
        }

        Visibility = Visibility.Visible;

        double trackWidth = Track.ActualWidth;
        if (trackWidth <= 0)
        {
            // The track has not been arranged yet, so its SizeChanged will bring us back here.
            return;
        }

        double visibleFraction = geometry.ViewportWidth / geometry.ContentWidth;
        double thumbWidth = Math.Max(Thumb.MinWidth, trackWidth * visibleFraction);
        thumbWidth = Math.Min(thumbWidth, trackWidth);

        double travel = trackWidth - thumbWidth;
        double scrolledFraction = Math.Clamp(geometry.Offset / scrollableWidth, 0, 1);

        Thumb.Width = thumbWidth;
        Thumb.Margin = new Thickness(travel * scrolledFraction, 0, 0, 0);
    }

    private void Track_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyGeometry();
    }

    private void Thumb_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragStartPointerX = e.GetCurrentPoint(Track).Position.X;
        _dragStartThumbOffset = Thumb.Margin.Left;
        _isDragging = Thumb.CapturePointer(e.Pointer);

        e.Handled = true;
    }

    private void Thumb_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging ||
            _geometry is null)
        {
            return;
        }

        double travel = Track.ActualWidth - Thumb.ActualWidth;
        if (travel <= 0)
        {
            return;
        }

        double scrollableWidth = _geometry.ContentWidth - _geometry.ViewportWidth;
        double pointerX = e.GetCurrentPoint(Track).Position.X;
        double thumbOffset = Math.Clamp(_dragStartThumbOffset + (pointerX - _dragStartPointerX), 0, travel);

        ScrollRequested?.Invoke(scrollableWidth * (thumbOffset / travel));

        e.Handled = true;
    }

    private void Thumb_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        Thumb.ReleasePointerCapture(e.Pointer);
        _isDragging = false;

        e.Handled = true;
    }

    private void Thumb_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
    }
}
