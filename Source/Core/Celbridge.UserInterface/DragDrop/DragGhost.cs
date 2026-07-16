using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Celbridge.UserInterface.DragDrop;

/// <summary>
/// A display-only drag ghost that follows the pointer during a pointer-driven drag: a label and an
/// optional caption (for example the move-or-copy action) in a rounded border. Rendered in a drag
/// overlay canvas, where display-only content composites cleanly over the native web views that the
/// built-in drag visuals clip against.
/// </summary>
public sealed class DragGhost
{
    private const double GhostOpacity = 0.85;
    private const double CursorOffset = 14.0;

    private readonly Canvas _overlay;
    private Border? _border;
    private TextBlock? _labelText;
    private TextBlock? _captionText;

    public DragGhost(Canvas overlay)
    {
        _overlay = overlay;
    }

    /// <summary>
    /// Creates the ghost with the given label and adds it to the overlay. The caption starts hidden.
    /// </summary>
    public void Show(string label)
    {
        _captionText = new TextBlock
        {
            Foreground = GetThemeBrush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray),
            FontSize = 11,
            Visibility = Visibility.Collapsed
        };

        _labelText = new TextBlock
        {
            Text = label,
            Foreground = GetThemeBrush("TextFillColorPrimaryBrush", Microsoft.UI.Colors.Black)
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical
        };
        stack.Children.Add(_captionText);
        stack.Children.Add(_labelText);

        _border = new Border
        {
            Background = GetThemeBrush("LayerFillColorDefaultBrush", Microsoft.UI.Colors.LightGray),
            BorderBrush = GetThemeBrush("ControlStrokeColorDefaultBrush", Microsoft.UI.Colors.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 4, 10, 4),
            Opacity = GhostOpacity,
            IsHitTestVisible = false,
            Child = stack
        };

        _overlay.Children.Add(_border);
    }

    /// <summary>
    /// Sets the caption line (for example the drop action), hiding it when null or empty.
    /// </summary>
    public void SetCaption(string? caption)
    {
        if (_captionText is null)
        {
            return;
        }

        _captionText.Text = caption ?? string.Empty;
        _captionText.Visibility = string.IsNullOrEmpty(caption) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Moves the ghost so it sits just off the pointer, clamped to the overlay bounds.
    /// </summary>
    public void Move(Point overlayPosition)
    {
        if (_border is null)
        {
            return;
        }

        double maxX = Math.Max(0, _overlay.ActualWidth - _border.ActualWidth);
        double maxY = Math.Max(0, _overlay.ActualHeight - _border.ActualHeight);
        double x = Math.Clamp(overlayPosition.X + CursorOffset, 0, maxX);
        double y = Math.Clamp(overlayPosition.Y + CursorOffset, 0, maxY);
        Canvas.SetLeft(_border, x);
        Canvas.SetTop(_border, y);
    }

    public void Hide()
    {
        if (_border is not null)
        {
            _overlay.Children.Remove(_border);
            _border = null;
            _labelText = null;
            _captionText = null;
        }
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
}
