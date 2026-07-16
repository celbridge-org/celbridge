using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

/// <summary>
/// Shared managed drag feedback for document sections: the insertion divider marking where a drop
/// will land in the tab strip, and the accent tint marking which section will receive it. Both are
/// display-only Borders in the shared drag overlay canvas, fed by the pointer-driven tab drag and by
/// resource drags from the Explorer so the two give the same feedback. Display-only overlays composite
/// cleanly over the native web views on the Skia head, which the built-in drag visuals do not.
/// </summary>
internal sealed class SectionDragPreview
{
    private const double IndicatorWidth = 2.0;

    private readonly DocumentSectionContainer _container;
    private readonly Canvas _overlay;
    private readonly Border _highlight;
    private readonly Border _insertionIndicator;

    public SectionDragPreview(DocumentSectionContainer container, Canvas overlay)
    {
        _container = container;
        _overlay = overlay;

        _highlight = new Border
        {
            Background = CreateAccentHighlightBrush(),
            BorderBrush = GetThemeBrush("AccentFillColorDefaultBrush", Microsoft.UI.Colors.DodgerBlue),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        _insertionIndicator = new Border
        {
            Width = IndicatorWidth,
            Background = GetThemeBrush("AccentFillColorDefaultBrush", Microsoft.UI.Colors.DodgerBlue),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        // The highlight sits behind the indicator, and both are added ahead of any per-drag ghost the
        // caller adds later, so the ghost always renders on top.
        _overlay.Children.Add(_highlight);
        _overlay.Children.Add(_insertionIndicator);
    }

    /// <summary>
    /// Shows the accent tint over the given section's content area, below its tab strip band. Moving it
    /// to another section is just another call: there is a single Border, so the previous section's
    /// highlight is replaced rather than duplicated.
    /// </summary>
    public void ShowHighlight(DocumentSection section)
    {
        var sectionBounds = GetElementBounds(section);
        if (sectionBounds.IsEmpty)
        {
            HideHighlight();
            return;
        }

        // The ghost tab of a tab drag rides in the strip band, so start the highlight just below the
        // band to avoid tinting it - most visible on an empty section, where the band is otherwise bare.
        var stripBand = ResolveStripBand(section);
        double top = stripBand.IsEmpty ? sectionBounds.Y : stripBand.Bottom;

        _highlight.Width = sectionBounds.Width;
        _highlight.Height = Math.Max(0, sectionBounds.Bottom - top);
        Canvas.SetLeft(_highlight, sectionBounds.X);
        Canvas.SetTop(_highlight, top);
        _highlight.Visibility = Visibility.Visible;
    }

    public void HideHighlight()
    {
        _highlight.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Shows the insertion divider at the given slot within the target section's tab strip. Pass the
    /// tab being dragged when the drag is a tab reorder within this section, so a drop that would not
    /// move it draws at that tab's own left edge; pass null for resource drags, which have no such tab.
    /// </summary>
    public void ShowInsertion(DocumentSection targetSection, int slot, DocumentTab? draggedTab)
    {
        var targetStrip = targetSection.GetTabStripBounds(_overlay);
        if (targetStrip.IsEmpty)
        {
            HideInsertion();
            return;
        }

        var headerBounds = targetSection.GetTabHeaderBounds(_overlay);
        int draggedIndex = draggedTab is null ? -1 : targetSection.GetTabIndex(draggedTab);

        // Position the divider at the slot boundary: the left edge of the slot's tab, the right edge of
        // the last tab for the trailing slot (append), or the strip start when empty. A slot adjacent to
        // the dragged tab's own position is a no-op drop, so mark that tab's left edge - where it stays.
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
            indicatorX = targetStrip.X;
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

        // The target's own strip collapses to a short height when the section is empty, so take the
        // divider's vertical extent from the strip band, which stays full height and shares the same
        // top edge as every section.
        var stripBand = ResolveStripBand(targetSection);

        _insertionIndicator.Height = stripBand.Height;
        Canvas.SetLeft(_insertionIndicator, indicatorX - (IndicatorWidth / 2));
        Canvas.SetTop(_insertionIndicator, stripBand.Y);
        _insertionIndicator.Visibility = Visibility.Visible;
    }

    public void HideInsertion()
    {
        _insertionIndicator.Visibility = Visibility.Collapsed;
    }

    public void Hide()
    {
        HideHighlight();
        HideInsertion();
    }

    /// <summary>
    /// Finds a full-height tab strip band to anchor the divider and highlight: the section's own strip
    /// when it has tabs, otherwise any populated section's strip. Sections sit side by side, so they
    /// share the strip's top edge and height. Returns Rect.Empty when no section has a laid-out strip.
    /// </summary>
    private Rect ResolveStripBand(DocumentSection preferred)
    {
        if (preferred.TabCount > 0)
        {
            var preferredBounds = preferred.GetTabStripBounds(_overlay);
            if (!preferredBounds.IsEmpty)
            {
                return preferredBounds;
            }
        }

        foreach (var section in _container.Sections)
        {
            if (section.TabCount == 0)
            {
                continue;
            }

            var bounds = section.GetTabStripBounds(_overlay);
            if (!bounds.IsEmpty)
            {
                return bounds;
            }
        }

        return Rect.Empty;
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

    private static Brush GetThemeBrush(string resourceKey, Windows.UI.Color fallbackColor)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallbackColor);
    }

    private static Brush CreateAccentHighlightBrush()
    {
        var color = GetThemeColor("AccentFillColorDefaultBrush", Microsoft.UI.Colors.DodgerBlue);
        color.A = 0x28;

        return new SolidColorBrush(color);
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
}
