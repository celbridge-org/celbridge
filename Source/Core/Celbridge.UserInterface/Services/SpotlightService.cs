using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Celbridge.UserInterface.Services;

public sealed class SpotlightService : ISpotlightService
{
    private readonly ILogger<SpotlightService> _logger;
    private readonly IMessengerService _messengerService;

    // The single active presenter, or null when none is registered.
    private ISpotlightPresenter? _presenter;

    // The element the active spotlight points at, retained so its one-shot interaction handler
    // can be detached when the spotlight clears.
    private FrameworkElement? _target;

    // Auto-clear timer for a spotlight created with a duration.
    private DispatcherTimer? _timer;

    public SpotlightService(
        ILogger<SpotlightService> logger,
        IMessengerService messengerService)
    {
        _logger = logger;
        _messengerService = messengerService;

        _messengerService.Register<ShowSpotlightMessage>(this, OnShowSpotlightMessage);
        _messengerService.Register<ClearSpotlightMessage>(this, OnClearSpotlightMessage);
    }

    public void RegisterPresenter(ISpotlightPresenter presenter)
    {
        if (ReferenceEquals(_presenter, presenter))
        {
            return;
        }

        // A new presenter replaces the previous one. Drop the spotlight state without driving the
        // outgoing presenter, which takes its spotlight with it.
        ClearSpotlightState();
        DetachPresenter();

        _presenter = presenter;
        _presenter.SpotlightClosed += OnSpotlightClosed;
    }

    public void UnregisterPresenter(ISpotlightPresenter presenter)
    {
        // Only the currently registered presenter may unregister, so a stale unregister after a
        // replacement does nothing.
        if (!ReferenceEquals(_presenter, presenter))
        {
            return;
        }

        // The presenter and its spotlight are going away, so drop the spotlight state without
        // driving the outgoing presenter.
        ClearSpotlightState();
        DetachPresenter();
        _presenter = null;
    }

    public void ShowSpotlight(FrameworkElement target, string label, int durationMs)
    {
        if (_presenter is null)
        {
            _logger.LogWarning("Cannot show spotlight: no spotlight presenter is registered.");
            return;
        }

        // Replace any active spotlight before retargeting the callout.
        ClearSpotlightState();

        _target = target;
        _presenter.ShowSpotlight(target, label);

        // Clear when the user interacts with the highlighted control ("you found it").
        target.Tapped += OnTargetTapped;

        if (durationMs > 0)
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }
    }

    public void ClearSpotlight()
    {
        ClearSpotlightState();
        _presenter?.HideSpotlight();
    }

    private void OnShowSpotlightMessage(object recipient, ShowSpotlightMessage message)
    {
        if (_presenter is null)
        {
            _logger.LogWarning($"Spotlight target '{message.Target}' cannot be shown: no spotlight presenter is registered.");
            return;
        }

        var element = _presenter.ResolveLandmark(message.Target);
        if (element is null)
        {
            _logger.LogWarning($"Spotlight target '{message.Target}' is catalogued but could not be resolved in the visual tree. The owning panel may not be open.");
            return;
        }

        ShowSpotlight(element, message.Label, message.DurationMs);
    }

    private void OnClearSpotlightMessage(object recipient, ClearSpotlightMessage message)
    {
        ClearSpotlight();
    }

    private void OnTargetTapped(object sender, TappedRoutedEventArgs e)
    {
        ClearSpotlight();
    }

    private void OnTimerTick(object? sender, object e)
    {
        ClearSpotlight();
    }

    private void OnSpotlightClosed(object? sender, EventArgs e)
    {
        // Covers the close button, light dismiss, and programmatic hides; releases any state
        // that outlived the visible spotlight.
        ClearSpotlightState();
    }

    private void DetachPresenter()
    {
        if (_presenter is not null)
        {
            _presenter.SpotlightClosed -= OnSpotlightClosed;
        }
    }

    // Detaches the interaction handler and stops the timer. Idempotent, so it is safe to call
    // from the tap handler, the timer tick, the spotlight-closed event, and a clear or retarget
    // request.
    private void ClearSpotlightState()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        if (_target is not null)
        {
            _target.Tapped -= OnTargetTapped;
            _target = null;
        }
    }
}
