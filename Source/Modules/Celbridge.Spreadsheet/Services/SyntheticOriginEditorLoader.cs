using System.Text.Json;
using Celbridge.Packages;
using Celbridge.Server;
using Celbridge.WebHost;
using Celbridge.WebHost.Platform;
using Celbridge.WebHost.Services;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Spreadsheet.Services;

/// <summary>
/// Loads the SpreadJS spreadsheet editor under a synthetic origin so its domain-locked licence validates:
/// a WebView2 virtual host on Windows, native loadHTMLString on the Uno Skia heads. The package's assets
/// (lib, shared client) are still served from the loopback file server cross-origin.
/// </summary>
public sealed class SyntheticOriginEditorLoader : IContributionEditorLoader
{
    private const string SpreadsheetPackageName = "celbridge.spreadsheet";
    private const string SyntheticHost = "spreadjs.celbridge";

    private readonly IFileServer _fileServer;
    private readonly ILocalFileSystem _localFileSystem;

    public SyntheticOriginEditorLoader(IFileServer fileServer, ILocalFileSystem localFileSystem)
    {
        _fileServer = fileServer;
        _localFileSystem = localFileSystem;
    }

    public bool CanLoad(PackageInfo package) => package.Name == SpreadsheetPackageName;

    public HostChannelTransport GetTransport(PackageInfo package)
    {
#if WINDOWS
        // The virtual-host page is not same-origin with the loopback server and cannot open the insecure
        // loopback WebSocket, so it falls back to the WebView2 message channel.
        return HostChannelTransport.WebView2Message;
#else
        // The loadHTMLString page gets the bridge URL injected, so it still uses the WebSocket.
        return HostChannelTransport.LoopbackWebSocket;
#endif
    }

    public string GetAllowedNavigationOrigin(ContributionEditorLoadRequest request) =>
#if WINDOWS
        // Windows navigates to the https virtual host.
        $"https://{SyntheticHost}/";
#else
        // The Skia heads load under the http synthetic origin via loadHTMLString.
        $"http://{SyntheticHost}/";
#endif

    public async Task LoadAsync(ContributionEditorLoadRequest request)
    {
#if WINDOWS
        await Task.CompletedTask;

        // Map the package folder to the synthetic-origin virtual host, then navigate to it. The licence
        // validates on the hostname.
        request.WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            SyntheticHost,
            request.Package.PackageFolder,
            CoreWebView2HostResourceAccessKind.Allow);

        var entryUrl = $"https://{SyntheticHost}/{request.EntryPoint}";
        entryUrl = HostChannelFactory.AppendConnectionToken(entryUrl, request.ConnectionToken);
        request.WebView.CoreWebView2.Navigate(entryUrl);
#else
        // The Skia heads create the WebView in place, so it is window-rooted and never re-parented: its
        // context is stable and the page can be loaded directly.
        var html = await BuildSyntheticOriginHtmlAsync(request);

        if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(request.WebView.CoreWebView2, out var handle, out var detail))
        {
            throw new InvalidOperationException($"Could not reach the native WKWebView handle for the synthetic-origin editor: {detail}");
        }

        // http (not https) origin so the cross-origin http loopback resource fetches are not blocked as mixed
        // content. The licence validates on the hostname, not the scheme.
        var syntheticOriginUrl = $"http://{SyntheticHost}/";
        MacOSWebViewInterop.LoadHtmlString(handle, html, syntheticOriginUrl);
#endif
    }

#if !WINDOWS
    /// <summary>
    /// Builds the entry page for native loadHTMLString:baseURL:. The entry HTML is rewritten so its lib and
    /// shared-client references resolve cross-origin to the loopback file server (absolute URLs, since
    /// loadHTMLString ignores a base element), and the WebSocket bridge URL is injected (the faked-origin page
    /// cannot derive it from its own location).
    /// </summary>
    private async Task<string> BuildSyntheticOriginHtmlAsync(ContributionEditorLoadRequest request)
    {
        var packageBaseUrl = _fileServer.GetPackageUrl(request.PackageUrlName, string.Empty);
        var assetsBaseUrl = $"http://127.0.0.1:{request.ServerPort}/assets/";
        var bridgeUrl = $"ws://127.0.0.1:{request.ServerPort}/ws/host?token={request.ConnectionToken}";

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
        // loopback /assets/ route, and the WebSocket bridge URL the faked-origin page cannot derive itself.
        var encodedBridgeUrl = JsonSerializer.Serialize(bridgeUrl);
        var importMap = $"<script type=\"importmap\">{{\"imports\":{{\"https://shared.celbridge/\":\"{assetsBaseUrl}\"}}}}</script>";
        var injectedHead = $"{importMap}<script>window.__celbridgeBridgeUrl={encodedBridgeUrl};</script>";

        return entryHtml.Replace("<head>", "<head>" + injectedHead);
    }
#endif
}
