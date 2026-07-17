using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

public sealed class SpotlightService : ISpotlightService
{
    private readonly ILogger<SpotlightService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ILayoutService _layoutService;
    private readonly ISpotlightRegistry _landmarkRegistry;

    // Reveal providers keyed by landmark id. Only landmarks that need preparation beyond the
    // default region reveal (for example the ephemeral Explorer toolbar) register one.
    private readonly Dictionary<string, ISpotlightLandmark> _landmarks = new();

    // The single active presenter, or null when none is registered.
    private ISpotlightPresenter? _presenter;

    // The element the active spotlight points at, retained so its one-shot interaction handler
    // can be detached when the spotlight clears.
    private FrameworkElement? _target;

    // The reveal provider of the active spotlight, retained so its transient reveal is undone
    // when the spotlight clears.
    private ISpotlightLandmark? _activeLandmark;

    // Auto-clear timer for a spotlight created with a duration.
    private DispatcherTimer? _timer;

    public SpotlightService(
        ILogger<SpotlightService> logger,
        IMessengerService messengerService,
        ILayoutService layoutService,
        ISpotlightRegistry landmarkRegistry)
    {
        _logger = logger;
        _messengerService = messengerService;
        _layoutService = layoutService;
        _landmarkRegistry = landmarkRegistry;

        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloadedMessage);
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

    public void RegisterLandmark(string landmarkId, ISpotlightLandmark landmark)
    {
        _landmarks[landmarkId] = landmark;
    }

    public void UnregisterLandmark(string landmarkId)
    {
        _landmarks.Remove(landmarkId);
    }

    public async Task<Result> ShowSpotlightAsync(string target, string label, int durationMs)
    {
        if (_presenter is null)
        {
            return Result.Fail($"Cannot show spotlight on '{target}': the workspace UI is not available.");
        }

        // Clear the previous spotlight (undoing its transient reveal) before preparing the new one.
        ClearSpotlightState();

        // Reveal the landmark's layout region, so spotlighting a landmark in a collapsed panel
        // opens that panel first. This reveal is sticky, matching the behaviour where spotlighting
        // the Inspector leaves it open.
        if (_landmarkRegistry.TryGetLandmark(target, out var descriptor) &&
            descriptor!.Region is not null)
        {
            _layoutService.SetRegionVisibility(descriptor.Region.Value, true);
        }

        // Run the landmark's own reveal, if it has a provider (for example switching to the
        // Explorer tab and fading its toolbar in). A failure means the landmark cannot be revealed.
        _landmarks.TryGetValue(target, out var landmark);
        if (landmark is not null)
        {
            var prepareResult = await landmark.PreSpotlightAsync();
            if (prepareResult.IsFailure)
            {
                return Result.Fail($"Cannot show spotlight on '{target}': the landmark could not be revealed.")
                    .WithErrors(prepareResult);
            }
        }

        var element = _presenter.ResolveLandmark(target);
        if (element is null)
        {
            landmark?.PostSpotlight();
            return Result.Fail($"Cannot show spotlight on '{target}': its control is not currently on screen. Open or reveal the relevant panel first.");
        }

        BeginSpotlight(element, label, durationMs, landmark);
        return Result.Ok();
    }

    public void ShowSpotlight(FrameworkElement target, string label, int durationMs)
    {
        ClearSpotlightState();
        BeginSpotlight(target, label, durationMs, null);
    }

    public void ClearSpotlight()
    {
        ClearSpotlightState();
        _presenter?.HideSpotlight();
    }

    private void BeginSpotlight(FrameworkElement target, string label, int durationMs, ISpotlightLandmark? landmark)
    {
        Guard.IsNotNull(_presenter);

        _target = target;
        _activeLandmark = landmark;
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

    private void OnWorkspaceUnloadedMessage(object recipient, WorkspaceUnloadedMessage message)
    {
        // A workspace landmark's control is gone once its workspace unloads, so drop any active
        // spotlight rather than leave it pointing at a torn-down element.
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

    // Detaches the interaction handler, stops the timer, and undoes the active landmark's transient
    // reveal. Idempotent, so it is safe to call from the tap handler, the timer tick, the
    // spotlight-closed event, and a clear or retarget request.
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

        if (_activeLandmark is not null)
        {
            _activeLandmark.PostSpotlight();
            _activeLandmark = null;
        }
    }
}
