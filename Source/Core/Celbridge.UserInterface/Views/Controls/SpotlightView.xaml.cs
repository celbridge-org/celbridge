using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Celbridge.UserInterface.Views.Controls;

public sealed partial class SpotlightView : UserControl, ISpotlightPresenter
{
    // The teaching tip's corner close button (40px, TeachingTipAlternateCloseButtonSize) floats
    // over the top-right of the content and reserves no layout space, so it overlaps the label.
    // The content already carries a 12px margin (TeachingTipContentMargin), so reserving the
    // remaining 28px keeps the label wrapping clear of the button. The button's own 4px padding
    // provides the visual gap between the text and the glyph.
    private const double CloseButtonClearance = 28;

    public SpotlightView()
    {
        InitializeComponent();

        var spotlightService = ServiceLocator.AcquireService<ISpotlightService>();
        spotlightService.RegisterPresenter(this);
    }

    public FrameworkElement? ResolveLandmark(string landmarkId)
    {
        // Resolve from the window content root so every landmark is reachable: the title-bar
        // chrome and whatever page is in the frame. Fall back to this control when the XamlRoot
        // is not available yet.
        var searchRoot = (XamlRoot?.Content as DependencyObject) ?? this;
        return VisualTreeHelperEx.FindDescendantByAutomationId(searchRoot, landmarkId);
    }

    public void ShowSpotlight(FrameworkElement target, string label)
    {
        SpotlightTeachingTip.Target = target;
        SpotlightTeachingTip.Title = string.Empty;
        SpotlightTeachingTip.Subtitle = string.Empty;

        // Render the label as content with an explicit white foreground rather than through the
        // subtitle, whose foreground resource is not honoured on the Skia head and left the text
        // black there. White reads correctly on the accent background on every head.
        if (string.IsNullOrEmpty(label))
        {
            SpotlightTeachingTip.Content = null;
        }
        else
        {
            SpotlightTeachingTip.Content = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, CloseButtonClearance, 0)
            };
        }

        SpotlightTeachingTip.IsOpen = true;
    }

    public void HideSpotlight()
    {
        SpotlightTeachingTip.IsOpen = false;
    }

    public event EventHandler? SpotlightClosed;

    private void SpotlightTeachingTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        SpotlightClosed?.Invoke(this, EventArgs.Empty);
    }
}
