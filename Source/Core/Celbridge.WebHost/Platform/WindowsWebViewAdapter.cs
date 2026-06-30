using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// IWebViewAdapter for the packaged Windows head, driving the WebView2 SDK directly.
/// </summary>
public sealed class WindowsWebViewAdapter : IWebViewAdapter
{
    // Bounds the wait for Page.captureScreenshot. Inactive WinUI tabs pause the WebView2 renderer, which would
    // otherwise leave the CDP call hanging.
    private static readonly TimeSpan ScreenshotCaptureTimeout = TimeSpan.FromSeconds(5);

    public bool CreatesWebViewInPlace => false;

    public bool RequiresPageUnloadBeforeClose => false;

    public bool UsesPrewarmedPool => true;

    public bool SupportsVirtualHostMapping => true;

    public async Task EnsureCoreWebView2Async(WebView2 webView)
    {
        // The packaged WebView2 initializes without being attached to the visual tree, so detached controls
        // (including the pre-warmed pool) work.
        await webView.EnsureCoreWebView2Async();
    }

    public void CloseWebView(WebView2 webView, Panel container)
    {
        container.Children.Remove(webView);
        webView.Close();
    }

    public async Task<string> EvalAsync(CoreWebView2 coreWebView2, string expression)
    {
        return await coreWebView2.ExecuteScriptAsync(expression);
    }

    public async Task ReloadAsync(CoreWebView2 coreWebView2, bool clearCache)
    {
        if (clearCache)
        {
            await coreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.DiskCache);
        }

        coreWebView2.Reload();
    }

    public async Task<ScreenshotData> CaptureScreenshotAsync(WebView2 webView, ScreenshotRequest request)
    {
        var coreWebView2 = webView.CoreWebView2;
        var paramsJson = BuildCaptureScreenshotParams(request);
        var captureTask = coreWebView2
            .CallDevToolsProtocolMethodAsync("Page.captureScreenshot", paramsJson)
            .AsTask();

        // Bounded wait so a tab switch mid-capture surfaces as a timeout instead of an indefinite hang.
        var winner = await Task.WhenAny(captureTask, Task.Delay(ScreenshotCaptureTimeout));
        if (winner != captureTask)
        {
            throw new TimeoutException(
                $"Screenshot timed out after {ScreenshotCaptureTimeout.TotalSeconds:0}s. " +
                "The document tab likely became inactive during capture, which pauses " +
                "WebView2 rendering. Re-activate the tab and retry.");
        }

        var resultJson = await captureTask;
        using var doc = JsonDocument.Parse(resultJson);
        var base64 = doc.RootElement.GetProperty("data").GetString() ?? string.Empty;
        // Decode at the platform boundary so downstream stages carry raw bytes. JSON envelopes can otherwise
        // escape '+' and corrupt the payload.
        var bytes = Convert.FromBase64String(base64);

        int width;
        int height;
        if (request.Clip is not null)
        {
            width = (int)Math.Round(request.Clip.Width * request.Clip.Scale);
            height = (int)Math.Round(request.Clip.Height * request.Clip.Scale);
        }
        else
        {
            width = 0;
            height = 0;
        }

        return new ScreenshotData(request.Format, width, height, bytes);
    }

    public void PostMessageToWeb(CoreWebView2 coreWebView2, string json)
    {
        coreWebView2.PostWebMessageAsString(json);
    }

    public async Task InstallDocumentStartScriptAsync(CoreWebView2 coreWebView2, string script)
    {
        await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }

    public async Task ReinjectDocumentStartScriptAsync(CoreWebView2 coreWebView2, string script)
    {
        // The managed document-start script persists across navigations, so re-injection is unnecessary here.
        await Task.CompletedTask;
    }

    public void LoadHtmlString(CoreWebView2 coreWebView2, string html, string baseUrl)
    {
        // The Windows head maps a virtual host to a real https origin instead, so this path is never taken.
        throw new NotSupportedException("LoadHtmlString is not used on the virtual-host-mapping head.");
    }

    public void SetApplicationUserAgent(CoreWebView2 coreWebView2, string applicationToken)
    {
        // The Chromium-based WebView2 UA is already recognised by sites, so only append the application token
        // for identification rather than replacing the UA.
        coreWebView2.Settings.UserAgent = $"{coreWebView2.Settings.UserAgent} {applicationToken}";
    }

    private static string BuildCaptureScreenshotParams(ScreenshotRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["format"] = request.Format
        };
        if (request.Format == "jpeg")
        {
            payload["quality"] = request.Quality;
        }
        if (request.Clip is not null)
        {
            payload["clip"] = new Dictionary<string, object>
            {
                ["x"] = request.Clip.X,
                ["y"] = request.Clip.Y,
                ["width"] = request.Clip.Width,
                ["height"] = request.Clip.Height,
                ["scale"] = request.Clip.Scale
            };
        }
        return JsonSerializer.Serialize(payload);
    }
}
