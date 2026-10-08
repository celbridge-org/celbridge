using Celbridge.Packages;

namespace Celbridge.WebHost;

/// <summary>
/// Loads a custom editor's entry page into its WebView and declares the origin the page is pinned to. The
/// default loads every editor over the loopback file server. A package may supply a custom loader for content
/// that must load under a different origin.
/// </summary>
public interface ICustomEditorLoader
{
    /// <summary>
    /// True when this loader handles the given package. The loopback default matches every package and is
    /// resolved as the fallback. A custom loader matches only its own package and is resolved ahead of it.
    /// </summary>
    bool CanLoad(PackageInfo package);

    /// <summary>
    /// The origin prefix the editor is pinned to. The view cancels any navigation outside it.
    /// </summary>
    string GetAllowedNavigationOrigin(CustomEditorLoadRequest request);

    /// <summary>
    /// Loads the editor's entry page, owning any platform-specific hosting needed to place it under its
    /// origin.
    /// </summary>
    Task LoadAsync(CustomEditorLoadRequest request);
}

/// <summary>
/// The inputs a loader needs to place a custom editor's entry page into its WebView. The view has
/// already created the control and brought up its CoreWebView2 before the loader runs. The page passes the
/// connection token back when it opens its WebSocket to the host.
/// </summary>
public sealed record CustomEditorLoadRequest(
    WebView2 WebView,
    PackageInfo Package,
    string PackageUrlName,
    string EntryPoint,
    string ConnectionToken,
    int ServerPort);
