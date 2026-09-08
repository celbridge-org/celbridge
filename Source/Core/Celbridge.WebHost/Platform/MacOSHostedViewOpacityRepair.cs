using Microsoft.UI.Dispatching;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Keeps a hosted WebView painted across the attaches it makes over its lifetime. No-op off macOS.
/// </summary>
// UNO-BUG: Uno sends a hosted native view's opacity to AppKit only while handling an opacity change on
// some element, and never as part of attaching the view. A view attached after the last such change keeps
// the alpha it was last sent - zero, for a document that was in a background tab - so the document is
// laid out and reachable but invisible until an unrelated opacity change happens to repaint it.
internal static class MacOSHostedViewOpacityRepair
{
    // A value the WebView is never left at: the first assignment is the opacity change Uno needs to see,
    // the second restores what the WebView actually renders at. Both land in one dispatcher turn, so
    // neither the faded WebView nor the intermediate value is ever presented.
    private const double NudgeOpacity = 0.99;

    /// <summary>
    /// Repairs the WebView every time it is placed in the visual tree. Called once, as the WebView is
    /// created, for the lifetime of the WebView.
    /// </summary>
    public static void RepairOnAttach(WebView2 webView)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        webView.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var webView = (WebView2)sender;

        // Loaded is raised before Uno has put the native view in place, the mirror of the teardown it does
        // after Unloaded, so the view is only there to be sent an opacity on the cycle that follows.
        webView.DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                // Uno recomputes every hosted view's opacity from the live visual tree on any opacity
                // change, so the round trip through a value it will not keep sends this one its real
                // opacity.
                var opacity = webView.Opacity;
                webView.Opacity = opacity == NudgeOpacity ? 1 : NudgeOpacity;
                webView.Opacity = opacity;
            });
    }
}
