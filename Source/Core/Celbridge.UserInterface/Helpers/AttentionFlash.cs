using Microsoft.UI.Xaml.Media.Animation;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Runs the shared attention flash: a brief accent-colour pulse on an overlay element that draws the eye to
/// something which just appeared, moved, or was asked for. The caller owns an overlay element with Opacity 0
/// and passes it in. The returned storyboard is already running, and the caller keeps it so a repeated flash
/// can stop the previous one.
/// </summary>
public static class AttentionFlash
{
    /// <summary>
    /// The peak opacity of a flash washing over an element, kept below full so the content underneath stays
    /// readable.
    /// </summary>
    public const double FillPeakOpacity = 0.55;

    /// <summary>
    /// The peak opacity of a flash tracing an outline, which covers no content and so pulses close to full.
    /// </summary>
    public const double OutlinePeakOpacity = 0.9;

    /// <summary>
    /// The thickness a perimeter flash draws its outline at, heavier than the chrome edge it pulses over.
    /// </summary>
    public const double OutlineThickness = 2;

    /// <summary>
    /// Starts a flash on the given overlay and returns the running storyboard. Pulses the overlay's opacity in
    /// to the given peak, holds, then fades out.
    /// </summary>
    public static Storyboard Play(UIElement overlay, double peakOpacity = FillPeakOpacity)
    {
        // One key-framed animation, not several: WinUI forbids two animations in a storyboard from targeting the
        // same property on the same element, so the fade-in, hold, and fade-out are key frames on a single
        // animation. Opacity is an independent (compositor) property, so no EnableDependentAnimation is needed
        // and it runs on both heads.
        var animation = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(animation, overlay);
        Storyboard.SetTargetProperty(animation, "Opacity");

        animation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
            Value = 0.0
        });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)),
            Value = peakOpacity
        });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(640)),
            Value = peakOpacity
        });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000)),
            Value = 0.0
        });

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();

        return storyboard;
    }

    /// <summary>
    /// Scales a chrome outline to the perimeter flash thickness, leaving bare every edge the chrome itself
    /// leaves bare.
    /// </summary>
    public static Thickness ResolveOutline(Thickness edges)
    {
        return new Thickness(
            ResolveOutlineEdge(edges.Left),
            ResolveOutlineEdge(edges.Top),
            ResolveOutlineEdge(edges.Right),
            ResolveOutlineEdge(edges.Bottom));
    }

    private static double ResolveOutlineEdge(double edge)
    {
        if (edge > 0)
        {
            return OutlineThickness;
        }

        return 0;
    }
}
