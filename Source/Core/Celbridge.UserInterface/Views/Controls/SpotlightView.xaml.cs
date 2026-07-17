using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Views.Controls;

public sealed partial class SpotlightView : UserControl, ISpotlightPresenter
{
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
        return VisualTree.FindDescendantByAutomationId(searchRoot, landmarkId);
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
            // Drop the label into its own row below the close button. The button glyph sits about
            // 12px in from the top and right edges; without this margin the content starts level with
            // the button and the gap beneath it reads as too tight. The default content margin already
            // supplies 12px, so 16px more clears the 40px button and leaves a matching 12px gap.
            SpotlightTeachingTip.Content = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0)
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
