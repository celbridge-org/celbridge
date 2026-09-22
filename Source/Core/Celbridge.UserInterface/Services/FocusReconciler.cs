using Celbridge.Logging;
using Celbridge.WebHost;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Derives the desired focus state from the focus model and applies it through the platform seams.
/// </summary>
public class FocusReconciler : IFocusReconciler
{
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;
    private readonly IManagedFocus _managedFocus;
    private readonly IHostWindowFocus _hostWindowFocus;
    private readonly ILogger<FocusReconciler> _logger;

    public FocusReconciler(
        IWebViewFocusRegistry webViewFocusRegistry,
        IManagedFocus managedFocus,
        IHostWindowFocus hostWindowFocus,
        ILogger<FocusReconciler> logger)
    {
        _webViewFocusRegistry = webViewFocusRegistry;
        _managedFocus = managedFocus;
        _hostWindowFocus = hostWindowFocus;
        _logger = logger;
    }

    public void Reconcile()
    {
        // Read once: the two rules below are arms of one classification, and a second read could observe
        // a different focused element.
        var managedFocusLocation = _managedFocus.FocusLocation;

        var desiredFocus = FocusDerivation.Derive(
            _webViewFocusRegistry.HasFocusedSurface,
            managedFocusLocation);

        _logger.LogTrace(
            "Focus reconcile: managed focus in {ManagedFocusLocation}, focus web surface {FocusWebSurface}, yield managed focus {YieldManagedFocus}",
            managedFocusLocation,
            desiredFocus.FocusWebSurface,
            desiredFocus.YieldManagedFocus);

        if (desiredFocus.YieldManagedFocus)
        {
            // Managed focus first: applying managed focus resigns the native first responder, so yielding
            // after the native step would undo the focus it establishes.
            _managedFocus.YieldFocus();
        }

        if (desiredFocus.FocusWebSurface)
        {
            _webViewFocusRegistry.FocusFocusedSurface();
            return;
        }

        _hostWindowFocus.FocusHostWindow();
    }
}
