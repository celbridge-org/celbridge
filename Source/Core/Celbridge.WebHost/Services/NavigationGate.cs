using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// Decides whether a navigation of a page may go ahead. isUserInitiated is true when the user started it, as
/// by clicking a link, and false when the page started it by itself. A gate is asked from inside the head's
/// own navigation machinery, so it must answer at once, and whatever a refusal calls for happens afterwards.
/// </summary>
public delegate bool NavigationGate(Uri destination, bool isUserInitiated);

/// <summary>
/// Puts each navigation the head starts to the gate, and cancels the ones it refuses. This is as early as a
/// head with no gate of its own can decide, and both WebView2 and Uno raise the event with the request for
/// the navigation already made ready, so cancelling here stops the page reaching the destination but leaves
/// the request to whatever else can stop it.
/// </summary>
internal sealed class NavigationStartingGate : IDisposable
{
    private readonly CoreWebView2 _coreWebView2;
    private readonly NavigationGate _gate;
    private readonly Action<Uri>? _onRefused;

    public NavigationStartingGate(CoreWebView2 coreWebView2, NavigationGate gate, Action<Uri>? onRefused = null)
    {
        _coreWebView2 = coreWebView2;
        _gate = gate;
        _onRefused = onRefused;

        _coreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
    }

    /// <summary>
    /// Whether the navigation the head is starting goes ahead. A refused destination is reported to
    /// onRefused, which is how a head that already has the request for it knows to stop that as well. An
    /// address the head names in some form other than an absolute URL is left alone.
    /// </summary>
    internal static bool Decides(string? url, bool isUserInitiated, NavigationGate gate, Action<Uri>? onRefused)
    {
        if (string.IsNullOrEmpty(url))
        {
            return true;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var destination))
        {
            return true;
        }

        if (gate(destination, isUserInitiated))
        {
            return true;
        }

        onRefused?.Invoke(destination);
        return false;
    }

    private void CoreWebView2_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Decides(args.Uri, args.IsUserInitiated, _gate, _onRefused))
        {
            args.Cancel = true;
        }
    }

    public void Dispose()
    {
        _coreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
    }
}
