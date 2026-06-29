using Celbridge.Packages;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// Loads a contribution editor's entry page into its WebView and declares the host-channel transport and
/// navigation origin that page can reach. The default loads every editor over the loopback file server; a
/// package may supply a custom loader for content that must load under a different origin.
/// </summary>
public interface IContributionEditorLoader
{
    /// <summary>
    /// True when this loader handles the given package. The loopback default matches every package and is
    /// resolved as the fallback; a custom loader matches only its own package and is resolved ahead of it.
    /// </summary>
    bool CanLoad(PackageInfo package);

    /// <summary>
    /// The host channel transport the editor's loaded page can reach.
    /// </summary>
    HostChannelTransport GetTransport(PackageInfo package);

    /// <summary>
    /// The origin prefix the editor is pinned to; the view cancels any navigation outside it.
    /// </summary>
    string GetAllowedNavigationOrigin(ContributionEditorLoadRequest request);

    /// <summary>
    /// Loads the editor's entry page, owning any platform-specific hosting needed to place it under its
    /// origin.
    /// </summary>
    Task LoadAsync(ContributionEditorLoadRequest request);
}

/// <summary>
/// The transport a contribution editor's page uses to reach the Celbridge host.
/// </summary>
public enum HostChannelTransport
{
    /// <summary>
    /// A WebSocket opened back to the loopback host server. Used by every page served from the loopback origin.
    /// </summary>
    LoopbackWebSocket,

    /// <summary>
    /// The WebView2 native message channel. Used by a page that is not same-origin with the loopback server
    /// and so cannot open the insecure loopback WebSocket.
    /// </summary>
    WebView2Message,
}

/// <summary>
/// The inputs a loader needs to place a contribution editor's entry page into its WebView.
/// </summary>
public sealed record ContributionEditorLoadRequest(
    CoreWebView2 CoreWebView2,
    PackageInfo Package,
    string PackageUrlName,
    string EntryPoint,
    string? ConnectionToken,
    int ServerPort);
