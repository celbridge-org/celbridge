using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A teaching tip whose corner close button glyph is recoloured to read against the accent-filled
/// callout background. The glyph colour is baked into the WinUI AlternateCloseButtonStyle through
/// theme resources that instance-level resource overrides do not reach on the Skia head, so the
/// colour is applied directly to the realised template part instead.
/// </summary>
public sealed class SpotlightCallout : TeachingTip
{
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("AlternateCloseButton") is Button closeButton)
        {
            var foreground = new SolidColorBrush(Colors.White);

            // The Normal state honours the button foreground directly. The pointer-over and pressed
            // states animate the glyph through theme brushes, so redirect those keys in the button's
            // own resource scope where the visual state manager resolves them.
            closeButton.Foreground = foreground;
            closeButton.Resources["TeachingTipAlternateCloseButtonForegroundPointerOver"] = foreground;
            closeButton.Resources["TeachingTipAlternateCloseButtonForegroundPressed"] = foreground;
        }
    }
}
