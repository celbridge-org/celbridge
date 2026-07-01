using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Services;

/// <summary>
/// The host channel to build a CelbridgeHost on, plus the per-transport details a view needs:
/// the optional connection token to embed in the page navigation URL (WebSocket transport only),
/// and the teardown to run when the view is disposed.
/// </summary>
public sealed record HostChannelSetup(IHostChannel Channel, string? ConnectionToken, Action Teardown);

/// <summary>
/// Builds the host channel for a WebView consumer, selecting between the WebView2 messaging transport
/// (WebViewHostChannel) and the loopback WebSocket transport (a ProxyHostChannel bound by the
/// host channel broker once the page connects). The caller picks the transport structurally: WebSocket
/// for first-party content served over loopback or under a synthetic origin (which loads the celbridge
/// client and opens the socket), WebView2 messaging for virtual-host or external content that cannot.
/// </summary>
public static class HostChannelFactory
{
    public static HostChannelSetup Create(
        CoreWebView2 coreWebView2,
        bool useWebSocketChannel,
        IHostChannelBroker hostChannelBroker)
    {
        if (useWebSocketChannel)
        {
            var pendingConnection = hostChannelBroker.CreatePendingConnection();
            var proxyChannel = pendingConnection.Channel;

            return new HostChannelSetup(proxyChannel, pendingConnection.Token, proxyChannel.Dispose);
        }

        var webViewChannel = new WebViewHostChannel(coreWebView2);

        return new HostChannelSetup(webViewChannel, null, webViewChannel.Detach);
    }

    /// <summary>
    /// Appends the WebSocket connection token to a navigation URL as a query parameter, or returns the
    /// URL unchanged when there is no token (the WebView2 transport). The page reads the token from
    /// location.search and opens its WebSocket with it.
    /// </summary>
    public static string AppendConnectionToken(string navigationUrl, string? connectionToken)
    {
        if (string.IsNullOrEmpty(connectionToken))
        {
            return navigationUrl;
        }

        var separator = navigationUrl.Contains('?') ? '&' : '?';

        return $"{navigationUrl}{separator}__celToken={connectionToken}";
    }
}
