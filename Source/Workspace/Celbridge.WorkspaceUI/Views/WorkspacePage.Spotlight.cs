using Celbridge.UserInterface.Helpers;

namespace Celbridge.WorkspaceUI.Views;

public sealed partial class WorkspacePage
{
    // The element the active spotlight points at, retained so its one-shot
    // interaction handler can be detached when the spotlight clears.
    private FrameworkElement? _spotlightTarget;

    // Auto-clear timer for a spotlight created with a duration.
    private DispatcherTimer? _spotlightTimer;

    private void OnShowSpotlight(object recipient, ShowSpotlightMessage message)
    {
        var element = VisualTreeHelperEx.FindDescendantByAutomationId(LayoutRoot, message.Target);
        if (element is null)
        {
            _logger.LogWarning($"Spotlight target '{message.Target}' is catalogued but could not be resolved in the visual tree. The owning panel may not be open.");
            return;
        }

        // Replace any active spotlight before retargeting the tip.
        ClearSpotlightState();

        _spotlightTarget = element;
        SpotlightTeachingTip.Target = element;
        SpotlightTeachingTip.Title = string.Empty;
        SpotlightTeachingTip.Subtitle = message.Label;
        SpotlightTeachingTip.IsOpen = true;

        // Clear when the user interacts with the highlighted control ("you found it").
        element.Tapped += OnSpotlightTargetTapped;

        if (message.DurationMs > 0)
        {
            _spotlightTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(message.DurationMs)
            };
            _spotlightTimer.Tick += OnSpotlightTimerTick;
            _spotlightTimer.Start();
        }
    }

    private void OnClearSpotlight(object recipient, ClearSpotlightMessage message)
    {
        SpotlightTeachingTip.IsOpen = false;
        ClearSpotlightState();
    }

    private void OnSpotlightTargetTapped(object sender, TappedRoutedEventArgs e)
    {
        SpotlightTeachingTip.IsOpen = false;
        ClearSpotlightState();
    }

    private void OnSpotlightTimerTick(object? sender, object e)
    {
        SpotlightTeachingTip.IsOpen = false;
        ClearSpotlightState();
    }

    private void SpotlightTeachingTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        // Covers the close button and programmatic closes; detaches handlers and
        // stops the timer so no spotlight state outlives the visible tip.
        ClearSpotlightState();
    }

    // Detaches the interaction handler and stops the timer. Idempotent, so it is
    // safe to call from the tap handler, the timer tick, the Closed event, and a
    // clear or retarget request.
    private void ClearSpotlightState()
    {
        if (_spotlightTimer is not null)
        {
            _spotlightTimer.Stop();
            _spotlightTimer.Tick -= OnSpotlightTimerTick;
            _spotlightTimer = null;
        }

        if (_spotlightTarget is not null)
        {
            _spotlightTarget.Tapped -= OnSpotlightTargetTapped;
            _spotlightTarget = null;
        }
    }
}
