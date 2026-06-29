using System.Text.Json;
using Celbridge.Server;
using Celbridge.WebHost.Services;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

// Platform-specific WebView hosting for contribution editors. Every editor is served over the loopback
// file server on every head; the lone exception is a synthetic-origin package, whose content must load under
// a fixed host (e.g. a domain-locked library licence). Windows fakes that origin with a WebView2 virtual
// host; the Uno Skia heads fake it natively with loadHTMLString, because SetVirtualHostNameToFolderMapping
// is a no-op there.
public sealed partial class ContributionDocumentView
{
    // True when the editor's package pins a synthetic origin instead of plain loopback serving.
    private bool HasSyntheticOrigin =>
        Contribution is not null
        && !string.IsNullOrEmpty(Contribution.Package.SyntheticOriginHost);

    // Windows maps the package folder to the synthetic-origin virtual host. The Skia heads no-op here and
    // establish the origin natively when the page loads (see LoadSyntheticOriginPageAsync).
    private void ConfigureSyntheticOriginHosting()
    {
#if WINDOWS
        if (!HasSyntheticOrigin)
        {
            return;
        }

        Guard.IsNotNull(Contribution);

        WebView!.CoreWebView2.SetVirtualHostNameToFolderMapping(
            Contribution.Package.SyntheticOriginHost,
            Contribution.Package.PackageFolder,
            CoreWebView2HostResourceAccessKind.Allow);
#endif
    }

    // Every loopback editor uses the WebSocket host channel. The synthetic-origin package uses it too on the
    // Skia heads (its loadHTMLString page gets the bridge URL injected), but on Windows its virtual-host page
    // is not same-origin with the loopback server and falls back to the WebView2 message channel.
    private bool ResolveUseWebSocketChannel()
    {
#if WINDOWS
        return !HasSyntheticOrigin;
#else
        return true;
#endif
    }

    // Loopback editors navigate to their /package/ URL on every head. The synthetic-origin package loads
    // under its faked origin instead -- via native loadHTMLString on the Skia heads, or by navigating to its
    // virtual-host origin on Windows.
    private async Task NavigateToEntryPointAsync(string packageUrlName, string entryPoint, string? connectionToken)
    {
        Guard.IsNotNull(Contribution);

#if !WINDOWS
        if (HasSyntheticOrigin)
        {
            // loadHTMLString into an unrendered webview is a no-op until the tab-refresh kick triggers a
            // render, so this must run inline during init.
            await LoadSyntheticOriginPageAsync(packageUrlName, entryPoint, connectionToken);
            return;
        }
#endif

        var fileServer = _serviceProvider.GetRequiredService<IFileServer>();
        var entryUrl = HasSyntheticOrigin
            ? $"https://{Contribution.Package.SyntheticOriginHost}/{entryPoint}"
            : fileServer.GetPackageUrl(packageUrlName, entryPoint);
        entryUrl = HostChannelFactory.AppendConnectionToken(entryUrl, connectionToken);
        WebView!.CoreWebView2.Navigate(entryUrl);

        await Task.CompletedTask;
    }

#if !WINDOWS
    /// <summary>
    /// Loads a synthetic-origin editor under its faked origin via native loadHTMLString:baseURL: on the Skia
    /// heads, so its domain-locked license passes. The entry HTML is rewritten so its lib and shared-client
    /// references resolve cross-origin to the loopback file server (absolute URLs, since loadHTMLString
    /// ignores a base element), and the WebSocket bridge URL is injected (the faked-origin page cannot derive
    /// it from its own location).
    /// </summary>
    private async Task LoadSyntheticOriginPageAsync(string packageUrlName, string entryPoint, string? connectionToken)
    {
        Guard.IsNotNull(Contribution);

        var coreWebView2 = WebView!.CoreWebView2;

        var fileServer = _serviceProvider.GetRequiredService<IFileServer>();
        var serverService = _serviceProvider.GetRequiredService<IServerService>();
        var localFileSystem = _serviceProvider.GetRequiredService<ILocalFileSystem>();

        var packageBaseUrl = fileServer.GetPackageUrl(packageUrlName, string.Empty);
        var assetsBaseUrl = $"http://127.0.0.1:{serverService.Port}/assets/";
        var bridgeUrl = $"ws://127.0.0.1:{serverService.Port}/ws/host?token={connectionToken}";

        var entryHtmlPath = System.IO.Path.Combine(Contribution.Package.PackageFolder, entryPoint);
        var readResult = await localFileSystem.ReadAllTextAsync(entryHtmlPath);
        if (readResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to read synthetic-origin entry HTML '{entryHtmlPath}': {readResult.DiagnosticReport}");
        }

        var encodedBridgeUrl = JsonSerializer.Serialize(bridgeUrl);

        // Rewrite the page's relative resource references to absolute loopback URLs. loadHTMLString does
        // not honour a <base> element, so the lib and entry script URLs are made absolute against the
        // package's loopback /package/ route.
        var entryHtml = readResult.Value
            .Replace("\"lib/", $"\"{packageBaseUrl}lib/")
            .Replace("\"spreadsheet.js\"", $"\"{packageBaseUrl}spreadsheet.js\"");

        // Inject into <head>: an import map remapping the editor's absolute shared.celbridge client
        // imports to the loopback /assets/ route (keeping the package's own files unchanged, so the
        // Windows virtual-host path still works), and the WebSocket bridge URL the faked-origin page
        // cannot derive itself.
        var importMap = $"<script type=\"importmap\">{{\"imports\":{{\"https://shared.celbridge/\":\"{assetsBaseUrl}\"}}}}</script>";
        var injectedHead = $"{importMap}<script>window.__celbridgeBridgeUrl={encodedBridgeUrl};</script>";
        var html = entryHtml.Replace("<head>", "<head>" + injectedHead);

        if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var handle, out var detail))
        {
            throw new InvalidOperationException($"Could not reach the native WKWebView handle for the synthetic-origin editor: {detail}");
        }

        // http (not https) origin so the cross-origin http loopback resource fetches are not blocked as
        // mixed content. The license validates on the hostname, not the scheme.
        var syntheticOriginUrl = $"http://{Contribution.Package.SyntheticOriginHost}/";
        MacOSWebViewInterop.LoadHtmlString(handle, html, syntheticOriginUrl);
    }
#endif
}
