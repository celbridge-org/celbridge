using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// The destinations a page has been refused, each held until the request for it arrives or its moment
/// passes. A refusal is spent on the first request it stops, so a destination the page is refused once and
/// allowed later is fetched the second time.
/// </summary>
internal sealed class RefusedDestinations
{
    // How long a refusal waits for the request it stops. WebView2 has the request ready as it raises
    // NavigationStarting, so it follows the refusal within moments, and one that nothing comes for is
    // dropped rather than left to stop a navigation the user later allows.
    private static readonly TimeSpan RefusalLifetime = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _timeProvider;

    // Each refused destination, in the form both a decision and a request normalize to, against the moment
    // the page was refused it.
    private readonly Dictionary<string, DateTimeOffset> _refusals = new(StringComparer.Ordinal);

    public RefusedDestinations(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Records that the page may not go to the destination.
    /// </summary>
    public void Refuse(Uri destination)
    {
        var now = _timeProvider.GetUtcNow();

        // Refusals nothing came for are dropped here rather than left to gather on a page that keeps trying
        // to leave.
        var expired = _refusals
            .Where(refusal => now - refusal.Value > RefusalLifetime)
            .Select(refusal => refusal.Key)
            .ToArray();

        foreach (var destinationUrl in expired)
        {
            _refusals.Remove(destinationUrl);
        }

        _refusals[destination.AbsoluteUri] = now;
    }

    /// <summary>
    /// Whether the request is for a destination the page was refused a moment ago, which spends the refusal.
    /// </summary>
    public bool Stops(string requestUrl)
    {
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var destination))
        {
            return false;
        }

        if (!_refusals.Remove(destination.AbsoluteUri, out var refusedAt))
        {
            return false;
        }

        return _timeProvider.GetUtcNow() - refusedAt <= RefusalLifetime;
    }
}

/// <summary>
/// Gates a page's navigations on the packaged Windows head. NavigationStarting is as early as WebView2
/// offers a decision, and it raises that event with the request already made ready and sends it even though
/// the navigation is cancelled there, so a destination refused at the decision is stopped again where
/// WebView2 intercepts the request and answered from the application instead of going out. Only what the
/// page itself asks the network for passes through that, so a destination answered from the head's cache,
/// or by a service worker, is not stopped.
/// </summary>
internal sealed class WebView2NavigationGate : IDisposable
{
    // Every address, since a refusal is what picks the requests to stop rather than the filter.
    private const string EveryDestination = "*";

    private readonly CoreWebView2 _coreWebView2;
    private readonly RefusedDestinations _refusedDestinations;
    private readonly NavigationStartingGate _navigationStartingGate;

    public WebView2NavigationGate(CoreWebView2 coreWebView2, NavigationGate gate)
        : this(coreWebView2, gate, TimeProvider.System)
    {
    }

    internal WebView2NavigationGate(CoreWebView2 coreWebView2, NavigationGate gate, TimeProvider timeProvider)
    {
        _coreWebView2 = coreWebView2;
        _refusedDestinations = new RefusedDestinations(timeProvider);
        _navigationStartingGate = new NavigationStartingGate(coreWebView2, gate, _refusedDestinations.Refuse);

        // Documents only, which is what a navigation asks for. The images, scripts and fetches a page makes
        // for itself are left alone.
        _coreWebView2.AddWebResourceRequestedFilter(EveryDestination, CoreWebView2WebResourceContext.Document);
        _coreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
    }

    private void CoreWebView2_WebResourceRequested(
        CoreWebView2 sender,
        CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (!_refusedDestinations.Stops(args.Request.Uri))
        {
            return;
        }

        // Answering the request is what stops it being sent. Nothing is shown for it: the navigation it
        // belongs to was cancelled where it was refused, so the page it would have replaced stays.
        args.Response = sender.Environment.CreateWebResourceResponse(
            null,
            403,
            "Forbidden",
            string.Empty);
    }

    public void Dispose()
    {
        _navigationStartingGate.Dispose();
        _coreWebView2.WebResourceRequested -= CoreWebView2_WebResourceRequested;
        _coreWebView2.RemoveWebResourceRequestedFilter(EveryDestination, CoreWebView2WebResourceContext.Document);
    }
}
