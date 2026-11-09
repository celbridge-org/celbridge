using Windows.Foundation;

namespace Celbridge.UserInterface.DragDrop;

/// <summary>
/// The accent divider that marks where a drop would land in a tab strip. A display-only Border in a drag overlay, so
/// the divider draws over the native web views on every head.
/// </summary>
public sealed class TabInsertionMarker
{
    private const double MarkerWidth = 2.0;

    private readonly Border _marker;

    public TabInsertionMarker(Canvas overlay)
    {
        _marker = new Border
        {
            Width = MarkerWidth,
            Background = GetAccentBrush(),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        overlay.Children.Add(_marker);
    }

    /// <summary>
    /// Shows the divider at an insertion slot's boundary. The divider spans the strip bounds vertically. A drop in a
    /// slot either side of the dragged tab leaves the tab in place. The divider then marks the dragged tab's leading
    /// edge. Pass -1 for the dragged index when the dragged item is not a tab of this strip.
    /// </summary>
    public void Show(Rect stripBounds, IReadOnlyList<Rect> tabBounds, int slot, int draggedIndex)
    {
        var markerX = GetMarkerX(stripBounds, tabBounds, slot, draggedIndex);

        _marker.Height = stripBounds.Height;
        Canvas.SetLeft(_marker, markerX - (MarkerWidth / 2));
        Canvas.SetTop(_marker, stripBounds.Y);
        _marker.Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        _marker.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Returns the X of the divider for an insertion slot. That is the leading edge of the slot's tab, or else the
    /// trailing edge of the tab before the slot, or else the strip's start. A tab not laid out has empty bounds.
    /// </summary>
    public static double GetMarkerX(Rect stripBounds, IReadOnlyList<Rect> tabBounds, int slot, int draggedIndex)
    {
        bool dropsInPlace = draggedIndex >= 0
            && (slot == draggedIndex || slot == draggedIndex + 1);

        if (dropsInPlace
            && TryGetLaidOutBounds(tabBounds, draggedIndex, out var draggedBounds))
        {
            return draggedBounds.X;
        }

        if (TryGetLaidOutBounds(tabBounds, slot, out var slotBounds))
        {
            return slotBounds.X;
        }

        if (TryGetLaidOutBounds(tabBounds, slot - 1, out var previousBounds))
        {
            return previousBounds.Right;
        }

        return stripBounds.X;
    }

    private static bool TryGetLaidOutBounds(IReadOnlyList<Rect> tabBounds, int index, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (index >= 0
            && index < tabBounds.Count)
        {
            bounds = tabBounds[index];
        }

        return !bounds.IsEmpty;
    }

    private static Brush GetAccentBrush()
    {
        if (Application.Current.Resources.TryGetValue("AccentBrush", out var resource)
            && resource is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
    }
}
