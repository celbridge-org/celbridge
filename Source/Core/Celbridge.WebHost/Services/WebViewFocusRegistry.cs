using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.UI.Dispatching;

namespace Celbridge.WebHost;

/// <summary>
/// One registration of a web view. The focus service compares registrations by reference, so each registration
/// is a separate surface.
/// </summary>
internal sealed class WebViewFocusRegistration : IFocusSurface
{
    public WebViewFocusRegistration(IWebView view, WebViewFocusContext context)
    {
        View = view;
        Context = context;
    }

    public IWebView View { get; }

    public WebViewFocusContext Context { get; }

    public string SurfaceName => View.Resource.ToString();
}

internal class WebViewFocusRegistry : IWebViewFocusRegistry
{
    private readonly IFocusService _focusService;
    private readonly IMessengerService _messengerService;
    private readonly ILogger<WebViewFocusRegistry> _logger;

    // Used only on the UI thread. Views register and close there, and focus signals are marshalled there.
    private readonly Dictionary<IWebView, WebViewFocusRegistration> _registrations = new();

    // The views whose events the registry has subscribed to.
    private readonly HashSet<IWebView> _observedViews = new();

    // The surface whose focus report is current. Cleared when the focus service releases the surface for another
    // surface or panel, and when the surface's view closes.
    private WebViewFocusRegistration? _focusedRegistration;

    // Whether the host window currently holds the keyboard. A page blurs both when focus moves to another
    // part of the application and when the whole window is deactivated, and only the first is focus leaving
    // the surface: alt-tabbing away must leave the caret where the user put it.
    private bool _isHostWindowActive = true;

    // Whether a modal dialog currently holds the keyboard. A dialog blurs the page exactly as a click on
    // another panel does, and only the host knows which it was.
    private bool _isModalDialogOpen;

    // Resolved lazily: the reconciler depends on this registry, so constructor-injecting it here would cycle.
    private IFocusReconciler? _focusReconciler;

    // A grant for a view that has not registered yet. The grant applies when the view registers, because a new
    // document is activated before its web view is ready. The grant is dropped if the user moves focus elsewhere
    // first, or if the view closes before registering.
    private IWebView? _pendingGrant;

    public WebViewFocusRegistry(
        IFocusService focusService,
        IMessengerService messengerService,
        ILogger<WebViewFocusRegistry> logger)
    {
        _focusService = focusService;
        _messengerService = messengerService;
        _logger = logger;

        _messengerService.Register<MainWindowActivatedMessage>(this, (_, _) => OnHostWindowActivationChanged(true));
        _messengerService.Register<MainWindowDeactivatedMessage>(this, (_, _) => OnHostWindowActivationChanged(false));
        _messengerService.Register<ModalDialogOpenedMessage>(this, (_, _) => _isModalDialogOpen = true);
        _messengerService.Register<ModalDialogClosedMessage>(this, (_, _) => _isModalDialogOpen = false);
    }

    private void OnHostWindowActivationChanged(bool isActive)
    {
        _isHostWindowActive = isActive;

        _logger.LogDebug("Host window {Activation}", isActive ? "activated" : "deactivated");
    }

    public void Register(IWebView view, WebViewFocusContext focusContext)
    {
        var registration = new WebViewFocusRegistration(view, focusContext);

        // A redock registers a live view under a new focus context, for example when a utility moves between the
        // Utility Panel and a document tab. The keyboard never left the view, so the focus model moves with the
        // view to the new panel.
        var replacesFocusedSurface =
            _registrations.TryGetValue(view, out var previousRegistration) &&
            ReferenceEquals(_focusedRegistration, previousRegistration);

        _registrations[view] = registration;
        ObserveView(view);

        if (replacesFocusedSurface)
        {
            // Granted rather than merely reported: the new registration is a different surface identity to
            // the focus service, so reporting it releases the old one, and releasing drops the page's caret.
            // The grant puts it back, which is what a redock should leave behind anyway.
            GrantFocus(view);
            return;
        }

        if (!ReferenceEquals(_pendingGrant, view))
        {
            return;
        }

        _pendingGrant = null;

        // The grant was issued for a panel the user has since moved away from, so applying it now would
        // pull the keyboard back off whatever they turned to while the surface was initializing.
        if (_focusService.FocusedPanel != focusContext.Panel)
        {
            _logger.LogDebug(
                "Dropped a deferred focus grant: focus moved to {Panel} while the surface was initializing",
                _focusService.FocusedPanel);
            return;
        }

        GrantFocus(view);
    }

    // Subscribed once per view, however many times it registers.
    private void ObserveView(IWebView view)
    {
        if (_observedViews.Add(view))
        {
            view.Closing += OnViewClosing;
            view.FocusGained += OnViewFocusGained;
            view.FocusLost += OnViewFocusLost;
        }
    }

    // Drops a closed view's registration.
    private void OnViewClosing(object? sender, EventArgs e)
    {
        if (sender is not IWebView view)
        {
            return;
        }

        view.Closing -= OnViewClosing;
        view.FocusGained -= OnViewFocusGained;
        view.FocusLost -= OnViewFocusLost;
        _observedViews.Remove(view);

        // A view can close before a deferred grant reaches it.
        if (ReferenceEquals(_pendingGrant, view))
        {
            _pendingGrant = null;
        }

        if (!_registrations.Remove(view, out var registration))
        {
            return;
        }

        if (ReferenceEquals(_focusedRegistration, registration))
        {
            _focusedRegistration = null;
            ClearFocusUnlessAnotherSurfaceClaims(registration);
        }

        // Clear the edit target on close so a closed editor cannot leave the Edit menu enabled. If a newer edit
        // target has already replaced this registration's target, the focus service keeps the newer target.
        _focusService.ClearEditTarget(registration.Context.EditTarget);
    }

    public void GrantFocus(IWebView view)
    {
        if (!_registrations.TryGetValue(view, out var registration))
        {
            // The view is still initializing, so hold the grant until the view registers. A later grant replaces
            // this pending grant, so the view the user acted on last takes focus.
            _pendingGrant = view;
            ObserveView(view);
            _logger.LogDebug("Focus granted to a web surface that has not registered yet; deferred until it does");

            return;
        }

        _pendingGrant = null;

        _logger.LogDebug("Focus granted to web surface {Surface}", registration.SurfaceName);

        // Reporting applies the claim, which also releases the previously focused surface immediately
        // rather than waiting for the JS focus round trip that a surface with no DOM-side grant never
        // produces. The optional DOM-side focus then places the caret.
        Report(registration);

        _ = registration.Context.GrantDomFocus?.Invoke();
    }

    // A view raises FocusGained when it gains managed focus and, on macOS, when a click lands inside it. A gain by
    // the surface that already holds the keyboard changes nothing.
    private void OnViewFocusGained(object? sender, EventArgs e)
    {
        if (sender is not IWebView view ||
            !_registrations.TryGetValue(view, out var registration) ||
            ReferenceEquals(_focusedRegistration, registration))
        {
            return;
        }

        Report(registration);
    }

    private void OnViewFocusLost(object? sender, EventArgs e)
    {
        if (sender is IWebView view &&
            _registrations.TryGetValue(view, out var registration))
        {
            OnFocusLost(registration);
        }
    }

    // Null for a view without a control, as in a test. Such a view takes part in the focus model only.
    private static DispatcherQueue? GetDispatcherQueue(IWebView view)
    {
        return (view as WebViewBase)?.Control?.DispatcherQueue;
    }

    // Clears focus after the focused surface closes, if no other surface has claimed focus by then. The clear is
    // deferred because closing a document activates the next document, which claims focus a step later. An
    // immediate clear would take the caret straight off that next document. The clear itself keeps the focus model
    // from naming a panel whose surface is gone, and the focus indicator from showing a caret on that panel.
    private void ClearFocusUnlessAnotherSurfaceClaims(WebViewFocusRegistration registration)
    {
        var dispatcherQueue = GetDispatcherQueue(registration.View);
        if (dispatcherQueue is null)
        {
            return;
        }

        // Deferred twice. Closing a document tears its surface down before it selects the next one, so a
        // single hop would run while the replacement's own grant was still queued behind it, and focus would
        // be seen to leave the panel and come straight back. The second hop puts this behind everything the
        // close queued.
        dispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () => dispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                () =>
                {
                    if (_focusedRegistration is not null)
                    {
                        return;
                    }

                    _logger.LogDebug(
                        "Cleared focus after the focused web surface {Surface} closed",
                        registration.SurfaceName);

                    _focusService.ClearFocus();
                }));
    }

    // The keyboard has left the page. A page can only ever report this for itself, so an untrusted document
    // gains nothing by forging it: the report is ignored unless that same surface currently holds focus.
    private void OnFocusLost(WebViewFocusRegistration registration)
    {
        // The window losing activation blurs the page just as a click on another panel does. The keyboard has
        // left the application rather than the surface, so the caret stays where the user put it and comes
        // back with them.
        if (!_isHostWindowActive)
        {
            _logger.LogDebug(
                "Ignored a focus loss from {Surface}: the host window is not active",
                registration.SurfaceName);
            return;
        }

        // The dialog took the keyboard, not another panel. Keeping the surface focused is what lets the
        // dialog hand the keyboard back to it on the way out.
        if (_isModalDialogOpen)
        {
            _logger.LogDebug(
                "Ignored a focus loss from {Surface}: a modal dialog holds the keyboard",
                registration.SurfaceName);
            return;
        }

        // The surface must still hold focus both when the report arrives and once the work the blur arrived
        // alongside has drained. The first check drops a report that raced past a release (the user left the
        // surface and came back while it was in flight); the deferred checks below decide the rest.
        if (!ReferenceEquals(_focusedRegistration, registration))
        {
            _logger.LogDebug(
                "Ignored a focus loss from {Surface}: it no longer holds focus",
                registration.SurfaceName);
            return;
        }

        var dispatcherQueue = GetDispatcherQueue(registration.View);
        if (dispatcherQueue is null)
        {
            return;
        }

        // Queued below the focus reconcile, which the resign that caused this blur queues at the same
        // priority and therefore ahead of it. Reading the settled state is what separates a blur the host
        // caused from one the user did.
        dispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                if (!ReferenceEquals(_focusedRegistration, registration))
                {
                    _logger.LogDebug(
                        "Ignored a focus loss from {Surface}: another surface claimed focus first",
                        registration.SurfaceName);
                    return;
                }

                if (HoldsPlatformKeyboardFocus(registration))
                {
                    _logger.LogDebug(
                        "Ignored a focus loss from {Surface}: the platform still routes the keyboard to it",
                        registration.SurfaceName);
                    return;
                }

                _logger.LogDebug(
                    "The focused web surface {Surface} reported that the keyboard left it",
                    registration.SurfaceName);

                _focusService.ClearFocus();
            });
    }

    // Whether the keyboard still belongs to this surface. A page reports a blur every time it loses focus, even
    // when the host moves focus away and hands it straight back. The page's report cannot tell that case from the
    // user clicking away. The platform can tell, so the view asks the platform.
    private static bool HoldsPlatformKeyboardFocus(WebViewFocusRegistration registration)
    {
        return (registration.View as WebViewBase)?.HoldsKeyboard() == true;
    }

    public bool IsRegisteredWebSurface(DependencyObject element)
    {
        return element is WebView2 webView
            && WebViewBase.FromControl(webView) is WebViewBase view
            && _registrations.ContainsKey(view);
    }

    public bool HasFocusedSurface => _focusedRegistration is not null;

    public bool IsFocusedSurface(IWebView view)
    {
        return ReferenceEquals(_focusedRegistration?.View, view);
    }

    public void FocusFocusedSurface()
    {
        var registration = _focusedRegistration;
        if (registration is null)
        {
            return;
        }

        _logger.LogTrace("Applying platform focus to web surface {Surface}", registration.SurfaceName);

        // Keyboard focus only: no report (app-level focus state has not changed) and no DOM-side grant
        // (the page's caret is exactly where the user put it and must not move).
        (registration.View as WebViewBase)?.FocusPage();
    }

    public bool TryForwardKeyEvent(IntPtr nativeKeyEvent)
    {
        if (_focusedRegistration?.View is not WebViewBase view)
        {
            return false;
        }

        return view.ForwardKeyDown(nativeKeyEvent);
    }

    public bool TryHandleTabKey(bool shift, IntPtr nativeKeyEvent)
    {
        var registration = _focusedRegistration;
        if (registration is null)
        {
            return false;
        }

        // The edit target comes from the registration, so the key stays with the surface holding the keyboard.
        if (registration.Context.EditTarget.TryHandleTabKey(shift))
        {
            return true;
        }

        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        // Deliver the key straight to the page so the page applies its own Tab behaviour, such as moving
        // between form fields. The key is reported handled even when it cannot be delivered. An unhandled Tab
        // would reach the managed focus loop, which moves focus out of the document.
        (registration.View as WebViewBase)?.SendKeyDown(nativeKeyEvent);

        return true;
    }

    private void Report(WebViewFocusRegistration registration)
    {
        _logger.LogTrace("Web surface {Surface} reported focus", registration.SurfaceName);

        var wasAlreadyFocused = ReferenceEquals(_focusedRegistration, registration);
        _focusedRegistration = registration;

        // Gaining focus is not the same as re-reporting focus already held, and only the first runs the
        // surface's side effect. A document's side effect makes it the active document, and the active
        // document changing is itself what carries the keyboard to it, so a side effect on every report
        // would leave the two driving each other without end.
        if (!wasAlreadyFocused)
        {
            registration.Context.OnFocusGained?.Invoke();
        }
        Action releaseFocus = () => ReleaseSurface(registration);
        var claim = FocusClaim.FromWebSurface(
            registration.Context.Panel,
            registration.Context.EditTarget,
            registration,
            releaseFocus);
        _focusService.OnFocusReceived(claim);

        // Reconcile on every claim, including those a click makes outside the grant path. A click inside a native web
        // view raises the view's focus gain while managed focus stays where it was. The reconcile moves the keys to
        // the page, away from the managed control the user last used. The focus model is updated first because the
        // reconciler derives its state from the model.
        _focusReconciler ??= ServiceLocator.AcquireService<IFocusReconciler>();
        _focusReconciler.Reconcile();
    }

    // The release callback handed to the focus service, invoked when another surface or panel claims focus.
    // Clears the focused-surface tracking (unless a newer report has already replaced it) before running the
    // surface's own release.
    private void ReleaseSurface(WebViewFocusRegistration registration)
    {
        if (ReferenceEquals(_focusedRegistration, registration))
        {
            _focusedRegistration = null;
        }

        registration.Context.ReleaseFocus();
    }
}
