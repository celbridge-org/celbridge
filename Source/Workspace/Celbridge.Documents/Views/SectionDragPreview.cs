using Celbridge.UserInterface.DragDrop;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

/// <summary>
/// Shared managed drag feedback for document sections: the insertion divider marking where a drop
/// will land in the tab strip, and the accent tint marking which section will receive it. Both are
/// display-only elements in the shared drag overlay canvas. The tab drag and resource drags from the
/// Explorer both feed them, so the two give the same feedback. Display-only overlays composite cleanly
/// over the native web views on the Skia head. The built-in drag visuals clip against those views.
/// </summary>
internal sealed class SectionDragPreview
{
    private readonly DocumentSectionContainer _container;
    private readonly Canvas _overlay;
    private readonly Border _highlight;
    private readonly TabInsertionMarker _insertionMarker;

    public SectionDragPreview(DocumentSectionContainer container, Canvas overlay)
    {
        _container = container;
        _overlay = overlay;

        _highlight = new Border
        {
            Background = CreateAccentHighlightBrush(),
            BorderBrush = GetThemeBrush("AccentBrush", Microsoft.UI.Colors.DodgerBlue),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        // The highlight sits behind the divider, and both are added ahead of any per-drag ghost the
        // caller adds later, so the ghost always renders on top.
        _overlay.Children.Add(_highlight);
        _insertionMarker = new TabInsertionMarker(_overlay);
    }

    /// <summary>
    /// Shows the accent tint over the given section's content area, below its tab strip band. Moving it
    /// to another section is just another call: there is a single Border, so the previous section's
    /// highlight is replaced rather than duplicated.
    /// </summary>
    public void ShowHighlight(DocumentSectionView sectionView)
    {
        var sectionBounds = GetElementBounds(sectionView);
        if (sectionBounds.IsEmpty)
        {
            HideHighlight();
            return;
        }

        // The ghost tab of a tab drag rides in the strip band, so start the highlight just below the
        // band to avoid tinting it - most visible on an empty section, where the band is otherwise bare.
        var stripBand = ResolveStripBand(sectionView);
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
    /// move it draws at that tab's own left edge. Pass null for resource drags, which have no such tab.
    /// </summary>
    public void ShowInsertion(DocumentSectionView targetSection, int slot, DocumentTab? draggedTab)
    {
        var targetStrip = targetSection.GetTabStripBounds(_overlay);
        if (targetStrip.IsEmpty)
        {
            HideInsertion();
            return;
        }

        var tabBounds = targetSection.GetTabBounds(_overlay);
        int draggedIndex = draggedTab is null ? -1 : targetSection.GetTabIndex(draggedTab);

        // An empty section's strip collapses to a short height. The divider therefore takes its height
        // and top from the strip band. The band stays full height, and every section's band shares the
        // same top edge.
        var stripBand = ResolveStripBand(targetSection);
        var markerBounds = stripBand.IsEmpty
            ? targetStrip
            : new Rect(targetStrip.X, stripBand.Y, targetStrip.Width, stripBand.Height);

        _insertionMarker.Show(markerBounds, tabBounds, slot, draggedIndex);
    }

    public void HideInsertion()
    {
        _insertionMarker.Hide();
    }

    public void Hide()
    {
        HideHighlight();
        HideInsertion();
    }

    /// <summary>
    /// Finds a tab strip band to anchor the divider and highlight: the section's own strip when it has
    /// tabs, otherwise any populated mounted section's strip. Returns Rect.Empty when no section has a
    /// laid-out strip.
    /// </summary>
    private Rect ResolveStripBand(DocumentSectionView preferred)
    {
        if (preferred.TabCount > 0)
        {
            var preferredBounds = preferred.GetTabStripBounds(_overlay);
            if (!preferredBounds.IsEmpty)
            {
                return preferredBounds;
            }
        }

        foreach (var sectionView in _container.GetMountedSections())
        {
            if (sectionView.TabCount == 0)
            {
                continue;
            }

            var bounds = sectionView.GetTabStripBounds(_overlay);
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
        var color = GetThemeColor("AccentBrush", Microsoft.UI.Colors.DodgerBlue);
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
