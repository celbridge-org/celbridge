using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Animation;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A small colored dot that marks a status on the corner of a tab or rail button glyph. The kind picks the
/// color, and a null kind hides the pip. A pulsing pip fades out and back in every few seconds, at the same
/// moments as every other pulsing pip.
/// </summary>
public sealed partial class StatusPip : UserControl
{
    // The time from the start of one fade to the start of the next.
    private static readonly TimeSpan PulsePeriod = TimeSpan.FromSeconds(4);

    // How long one fade takes, from full opacity out to clear and back.
    private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(1);

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(PipKind?),
        typeof(StatusPip),
        new PropertyMetadata(null, OnKindChanged));

    public static readonly DependencyProperty IsPulsingProperty = DependencyProperty.Register(
        nameof(IsPulsing),
        typeof(bool),
        typeof(StatusPip),
        new PropertyMetadata(false, OnIsPulsingChanged));

    private DispatcherQueueTimer? _fadeTimer;
    private Storyboard? _fadeStoryboard;

    public StatusPip()
    {
        this.InitializeComponent();

        // Re-apply the visual state once loaded, so a kind set before the control entered the live visual
        // tree still draws its color, and a pip set to pulse starts pulsing.
        Loaded += (sender, e) => UpdateKindVisualState();

        // Moving a pip to a new parent can raise Unloaded for the old parent after Loaded for the new one. The
        // pulse therefore stops only for a pip that is not loaded.
        Unloaded += (sender, e) => UpdatePulse();
    }

    /// <summary>
    /// The kind of status the pip shows, or null to hide the pip.
    /// </summary>
    public PipKind? Kind
    {
        get => (PipKind?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>
    /// Set to true to make the pip fade out and back in every few seconds while it shows a kind.
    /// </summary>
    public bool IsPulsing
    {
        get => (bool)GetValue(IsPulsingProperty);
        set => SetValue(IsPulsingProperty, value);
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((StatusPip)d).UpdateKindVisualState();
    }

    private static void OnIsPulsingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((StatusPip)d).UpdatePulse();
    }

    private void UpdateKindVisualState()
    {
        var kind = Kind;
        if (kind is null)
        {
            Visibility = Visibility.Collapsed;
        }
        else
        {
            VisualStateManager.GoToState(this, kind.Value.ToString(), false);
            Visibility = Visibility.Visible;
        }

        UpdatePulse();
    }

    private bool ShouldPulse => IsPulsing
        && Kind is not null
        && IsLoaded;

    private void UpdatePulse()
    {
        if (!ShouldPulse)
        {
            StopPulse();
            return;
        }

        if (_fadeTimer is not null
            && _fadeTimer.IsRunning)
        {
            return;
        }

        // A pip that starts to pulse stays at full opacity until the next multiple of the period.
        ScheduleFade(TimeSpan.Zero);
    }

    // Waits for the first whole multiple of the period later than the given time from now. Each fade is timed
    // from the clock afresh, so a stalled UI thread leaves the pips in step.
    private void ScheduleFade(TimeSpan notBefore)
    {
        if (_fadeTimer is null)
        {
            _fadeTimer = DispatcherQueue.CreateTimer();
            _fadeTimer.IsRepeating = false;
            _fadeTimer.Tick += FadeTimer_Tick;
        }

        _fadeTimer.Interval = PulseSchedule.GetDelayToNextFade(Environment.TickCount64, notBefore, PulsePeriod);
        _fadeTimer.Start();
    }

    private void FadeTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        // A tick queued before the pulse stopped still arrives, and leaves the pulse stopped.
        if (!ShouldPulse)
        {
            return;
        }

        PlayFade();

        // The next fade waits until this one has played, so a tick that lands a little early plays one fade.
        ScheduleFade(FadeDuration);
    }

    private void PlayFade()
    {
        _fadeStoryboard?.Stop();

        // One key-framed animation on the ellipse's opacity: out to clear at the midpoint, then back to full.
        var animation = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(animation, PipEllipse);
        Storyboard.SetTargetProperty(animation, "Opacity");

        animation.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
            Value = 1.0
        });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(FadeDuration / 2),
            Value = 0.0,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(FadeDuration),
            Value = 1.0,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        });

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();

        _fadeStoryboard = storyboard;
    }

    private void StopPulse()
    {
        _fadeTimer?.Stop();

        // Stopping the storyboard returns the ellipse to full opacity.
        _fadeStoryboard?.Stop();
        _fadeStoryboard = null;
    }
}
