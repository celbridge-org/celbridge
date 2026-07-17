using Microsoft.UI.Xaml.Media.Animation;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Runs the shared attention flash: a brief accent-colour pulse on an overlay element that draws the eye to a
/// surface which just appeared or moved (a docked utility tab, a document tab moved by a section-count change,
/// or a utility rail button freed by an undock). The caller owns an overlay element with Opacity 0 and passes
/// it in; the returned storyboard is already running, and the caller keeps it so a repeated flash can stop the
/// previous one.
/// </summary>
public static class AttentionFlash
{
    /// <summary>
    /// Starts a flash on the given overlay and returns the running storyboard. Pulses the overlay's opacity in
    /// to a partial accent wash (kept below full so any content above it stays readable), holds, then fades out.
    /// </summary>
    public static Storyboard Play(UIElement overlay)
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
            Value = 0.55
        });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)),
            Value = 0.55
        });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(920)),
            Value = 0.0
        });

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();

        return storyboard;
    }
}
