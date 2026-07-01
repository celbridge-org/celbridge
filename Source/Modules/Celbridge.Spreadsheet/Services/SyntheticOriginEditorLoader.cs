using System.Text.Json;
using Celbridge.Packages;
using Celbridge.Server;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Spreadsheet.Services;

/// <summary>
/// Loads the SpreadJS spreadsheet editor under a synthetic origin so its domain-locked licence validates:
/// a WebView2 virtual host on the Windows heads, native loadHTMLString on the macOS and Linux Skia heads.
/// Either way the page is a faked origin; its lib and the shared client resolve cross-origin.
/// </summary>
public sealed class SyntheticOriginEditorLoader : IContributionEditorLoader
{
    private const string SpreadsheetPackageName = "celbridge.spreadsheet";
    private const string SyntheticHost = "spreadjs.celbridge";

    private readonly IFileServer _fileServer;
    private readonly ILocalFileSystem _localFileSystem;
    private readonly IWebViewAdapter _webViewAdapter;

    public SyntheticOriginEditorLoader(
        IFileServer fileServer,
        ILocalFileSystem localFileSystem,
        IWebViewAdapter webViewAdapter)
    {
        _fileServer = fileServer;
        _localFileSystem = localFileSystem;
        _webViewAdapter = webViewAdapter;
    }

    public bool CanLoad(PackageInfo package) => package.Name == SpreadsheetPackageName;

    // Both origin-faking mechanisms (the WebView2 virtual host and WKWebView loadHTMLString) produce an http
    // faked origin that receives the bridge URL, so every head reaches the host over the loopback WebSocket.
    // Only the mechanism in LoadAsync differs by platform.
    public HostChannelTransport GetTransport(PackageInfo package) => HostChannelTransport.LoopbackWebSocket;

    // http (not https) on every head: the faked-origin page must open the insecure loopback WebSocket and
    // fetch the http loopback assets without a mixed-content block. The licence validates on the hostname.
    public string GetAllowedNavigationOrigin(ContributionEditorLoadRequest request) => $"http://{SyntheticHost}/";

    public async Task LoadAsync(ContributionEditorLoadRequest request)
    {
        // The faked-origin page fetches its lib and the shared client cross-origin from the loopback file
        // server, so allow that specific origin to read /assets/ and /package/. No effect on the Windows
        // heads (their virtual-host mapping serves the same content same-origin), but harmless there.
        _fileServer.RegisterCrossOriginReader($"http://{SyntheticHost}");

        if (_webViewAdapter.SupportsVirtualHostMapping)
        {
            // The Windows heads map the package folder to the synthetic-origin virtual host and navigate to it
            // over http. The licence validates on the hostname, not the scheme.
            request.WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                SyntheticHost,
                request.Package.PackageFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            // The virtual-host page is a faked origin and cannot derive the loopback socket URL from its own
            // location, so the full host channel URL is passed as a query parameter it reads synchronously. A
            // document-start global would be cleaner, but the Skia WebView2 does not implement that API.
            var hostChannelUrl = $"ws://127.0.0.1:{request.ServerPort}/ws/host?token={request.ConnectionToken}";
            var entryUrl = $"http://{SyntheticHost}/{request.EntryPoint}?__hostChannelUrl={Uri.EscapeDataString(hostChannelUrl)}";
            request.WebView.CoreWebView2.Navigate(entryUrl);
            return;
        }

        // The Skia heads create the WebView in place, so it is window-rooted and never re-parented: its
        // context is stable and the page can be loaded directly.
        var html = await BuildSyntheticOriginHtmlAsync(request);

        // http (not https) origin so the cross-origin http loopback resource fetches are not blocked as mixed
        // content. The licence validates on the hostname, not the scheme.
        var syntheticOriginUrl = $"http://{SyntheticHost}/";
        _webViewAdapter.LoadHtmlString(request.WebView.CoreWebView2, html, syntheticOriginUrl);
    }

    /// <summary>
    /// Builds the entry page for native loadHTMLString:baseURL:. The entry HTML is rewritten so its lib and
    /// shared-client references resolve cross-origin to the loopback file server (absolute URLs, since
    /// loadHTMLString ignores a base element), and the WebSocket host channel URL is injected (the faked-origin
    /// page cannot derive it from its own location).
    /// </summary>
    private async Task<string> BuildSyntheticOriginHtmlAsync(ContributionEditorLoadRequest request)
    {
        var packageBaseUrl = _fileServer.GetPackageUrl(request.PackageUrlName, string.Empty);
        var assetsBaseUrl = $"http://127.0.0.1:{request.ServerPort}/assets/";
        var hostChannelUrl = $"ws://127.0.0.1:{request.ServerPort}/ws/host?token={request.ConnectionToken}";

        var entryHtmlPath = System.IO.Path.Combine(request.Package.PackageFolder, request.EntryPoint);
        var readResult = await _localFileSystem.ReadAllTextAsync(entryHtmlPath);
        if (readResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to read synthetic-origin entry HTML '{entryHtmlPath}': {readResult.DiagnosticReport}");
        }

        // Rewrite the page's relative resource references to absolute loopback URLs. loadHTMLString does not
        // honour a <base> element, so the lib and entry script URLs are made absolute against the package's
        // loopback /package/ route.
        var entryHtml = readResult.Value
            .Replace("\"lib/", $"\"{packageBaseUrl}lib/")
            .Replace("\"spreadsheet.js\"", $"\"{packageBaseUrl}spreadsheet.js\"");

        // Inject into <head>: an import map remapping the absolute shared.celbridge client imports to the
        // loopback /assets/ route, and the WebSocket host channel URL the faked-origin page cannot derive itself.
        var encodedHostChannelUrl = JsonSerializer.Serialize(hostChannelUrl);
        var importMap = $"<script type=\"importmap\">{{\"imports\":{{\"https://shared.celbridge/\":\"{assetsBaseUrl}\"}}}}</script>";
        var injectedHead = $"{importMap}<script>window.__hostChannelUrl={encodedHostChannelUrl};</script>";

        return entryHtml.Replace("<head>", "<head>" + injectedHead);
    }
}
