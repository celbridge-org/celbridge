using Celbridge.Messaging;
using Celbridge.Platform;
using Celbridge.UserInterface.Services;
using Celbridge.WebHost;

namespace Celbridge.UserInterface.Platform;

internal sealed class OverlayFlyoutSupport : IOverlayFlyoutSupport
{
    private readonly IFocusReconciler _focusReconciler;
    private readonly IPlatformInfo _platformInfo;

    private bool _isHostWindowActive = true;

    public OverlayFlyoutSupport(
        IFocusReconciler focusReconciler,
        IPlatformInfo platformInfo,
        IMessengerService messengerService)
    {
        _focusReconciler = focusReconciler;
        _platformInfo = platformInfo;

        messengerService.Register<MainWindowActivatedMessage>(this, (_, _) => _isHostWindowActive = true);
        messengerService.Register<MainWindowDeactivatedMessage>(this, (_, _) => _isHostWindowActive = false);
    }

    public void Apply(FlyoutBase flyout)
    {
        // The scope lives in the handlers the flyout itself holds, so nothing outside the flyout keeps it
        // alive and a per-instance flyout (a document tab's context menu) is collected with its owner.
        IDisposable? suppressionScope = null;

        flyout.Opened += (_, _) =>
        {
            // Defensive: a second Opened without an intervening Closed would otherwise strand a scope, and
            // a stranded scope leaves every hosted web view ignoring the mouse for the rest of the session.
            suppressionScope?.Dispose();
            suppressionScope = MacOSWebViewInputSuppressor.Suppress();
        };

        flyout.Closed += (_, _) =>
        {
            suppressionScope?.Dispose();
            suppressionScope = null;

            // Both corrections below are for the head where hosted web views take focus and mouse input
            // natively. Elsewhere a web view takes part in managed focus, the toolkit hands the keyboard
            // back when a popup closes, and there is nothing to undo.
            if (!_platformInfo.HostedWebViewFocusIsNative)
            {
                return;
            }

            // Uno raises Closed only for the outermost open flyout, so a flyout dismissed underneath
            // another never disposes its own scope. Nothing overlays the web views once the popups are all
            // gone, which is the moment any scope left behind can be dropped.
            if (flyout.XamlRoot is not null
                && VisualTreeHelper.GetOpenPopupsForXamlRoot(flyout.XamlRoot).Count == 0)
            {
                MacOSWebViewInputSuppressor.ReleaseAll();
            }

            // A flyout light-dismissed by a click into another application closes with the keyboard
            // already elsewhere, and driving focus then takes the caret the user left behind.
            if (!_isHostWindowActive)
            {
                return;
            }

            // Uno leaves managed focus on the flyout item it has already taken out of the visual tree, so
            // the keys typed next reach a dismissed menu. Queued below Uno's own focus work, which reports
            // the accompanying focus change after the resign that precedes it.
            flyout.DispatcherQueue?.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => _focusReconciler.Reconcile());
        };
    }
}
