// The packaged Windows head is the only head that uses this adapter, and its find implementation depends on
// CoreWebView2Find, a type present only in the WinAppSDK-referenced WebView2 (1.0.3912.50) and absent from the
// older WebView2 the Uno Skia heads reference. Compiling the whole adapter under WINDOWS aligns compilation
// with the DI selection in PlatformServiceConfiguration, so the Skia build never sees the missing type.
#if WINDOWS
using System.Reflection;
using System.Text.Json;
using Celbridge.Logging;
using Microsoft.UI.Input;
using Microsoft.Web.WebView2.Core;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// IWebViewAdapter for the packaged Windows head, driving the WebView2 SDK directly.
/// </summary>
public sealed class WindowsWebViewAdapter : IWebViewAdapter
{
    private readonly ILogger<WindowsWebViewAdapter> _logger;

    // Bounds the wait for Page.captureScreenshot. Inactive WinUI tabs pause the WebView2 renderer, which would
    // otherwise leave the CDP call hanging.
    private static readonly TimeSpan ScreenshotCaptureTimeout = TimeSpan.FromSeconds(5);

    public WindowsWebViewAdapter(ILogger<WindowsWebViewAdapter> logger)
    {
        _logger = logger;
    }

    public bool SupportsVirtualHostMapping => true;

    // The find methods receive only a CoreWebView2, so sessions are keyed by it to recover per-find state.
    private readonly Dictionary<CoreWebView2, FindSession> _findSessions = new();

    private sealed record FindSession(
        CoreWebView2Find Find,
        EventHandler<object> OnMatchCountChanged,
        EventHandler<object> OnActiveMatchIndexChanged);

    public async Task EnsureCoreWebView2Async(WebView2 webView)
    {
        // The packaged WebView2 initializes without being attached to the visual tree, so detached controls
        // (including the pre-warmed pool) work.
        await webView.EnsureCoreWebView2Async();
    }

    public void CloseWebView(WebView2 webView, Panel container)
    {
        if (webView.CoreWebView2 is not null)
        {
            StopFind(webView.CoreWebView2);
        }

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
        // for identification rather than replacing the UA. Not idempotent: call once per WebView, since a
        // second call would append the token again.
        coreWebView2.Settings.UserAgent = $"{coreWebView2.Settings.UserAgent} {applicationToken}";
    }

    public async Task StartFindAsync(CoreWebView2 coreWebView2, string term, FindOptions options)
    {
        // Restart cleanly so a prior session's handlers cannot double-report against the new term.
        StopFind(coreWebView2);

        if (string.IsNullOrEmpty(term))
        {
            return;
        }

        var find = coreWebView2.Find;

        var findOptions = coreWebView2.Environment.CreateFindOptions();
        findOptions.FindTerm = term;
        findOptions.IsCaseSensitive = options.CaseSensitive;
        findOptions.ShouldMatchWord = false;
        // Our host bar is the only find UI. Suppressing Chromium's built-in bar keeps them from both showing.
        findOptions.SuppressDefaultFindDialog = true;

        void ReportState()
        {
            var matchCount = find.MatchCount;
            var activeMatchIndex = find.ActiveMatchIndex;
            var state = new FindMatchState(matchCount > 0, matchCount, activeMatchIndex);
            options.OnMatchStateChanged?.Invoke(state);
        }

        EventHandler<object> onMatchCountChanged = (_, _) => ReportState();
        EventHandler<object> onActiveMatchIndexChanged = (_, _) => ReportState();
        find.MatchCountChanged += onMatchCountChanged;
        find.ActiveMatchIndexChanged += onActiveMatchIndexChanged;

        _findSessions[coreWebView2] = new FindSession(find, onMatchCountChanged, onActiveMatchIndexChanged);

        await find.StartAsync(findOptions);

        // Report once after the session starts in case the counts settled before a change event fired.
        ReportState();
    }

    public void FindNext(CoreWebView2 coreWebView2)
    {
        if (_findSessions.TryGetValue(coreWebView2, out var session))
        {
            session.Find.FindNext();
        }
    }

    public void FindPrevious(CoreWebView2 coreWebView2)
    {
        if (_findSessions.TryGetValue(coreWebView2, out var session))
        {
            session.Find.FindPrevious();
        }
    }

    public void StopFind(CoreWebView2 coreWebView2)
    {
        if (!_findSessions.Remove(coreWebView2, out var session))
        {
            return;
        }

        session.Find.MatchCountChanged -= session.OnMatchCountChanged;
        session.Find.ActiveMatchIndexChanged -= session.OnActiveMatchIndexChanged;
        session.Find.Stop();
    }

    public void InstallFindShortcut(WebView2 webView, Action openFindBar)
    {
        // The WinUI WebView2 surfaces only CoreWebView2, not the CoreWebView2Controller that raises
        // AcceleratorKeyPressed, so reach the controller by reflecting over the control's non-public fields
        // (the same reach-into-the-runtime approach the macOS interop uses). Degrade to a no-op -- Ctrl+F then
        // falls back to Chromium's built-in bar -- rather than crash if the field shape changes.
        try
        {
            var controller = ResolveController(webView);
            if (controller is null)
            {
                _logger.LogWarning("Could not resolve the CoreWebView2Controller; the host find shortcut is not installed");
                return;
            }

            controller.AcceleratorKeyPressed += (_, args) =>
            {
                if (args.KeyEventKind != CoreWebView2KeyEventKind.KeyDown)
                {
                    return;
                }

                if (args.VirtualKey != (uint)VirtualKey.F)
                {
                    return;
                }

                var controlDown = InputKeyboardSource
                    .GetKeyStateForCurrentThread(VirtualKey.Control)
                    .HasFlag(CoreVirtualKeyStates.Down);
                if (!controlDown)
                {
                    return;
                }

                // Handle only this one key so print (Ctrl+P), reload (Ctrl+R), zoom (Ctrl+/-), and every other
                // browser accelerator stay with the browser.
                args.Handled = true;
                webView.DispatcherQueue.TryEnqueue(() => openFindBar());
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install the host find shortcut");
        }
    }

    private static CoreWebView2Controller? ResolveController(WebView2 webView)
    {
        var fields = webView.GetType().GetFields(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

        foreach (var field in fields)
        {
            if (typeof(CoreWebView2Controller).IsAssignableFrom(field.FieldType))
            {
                return field.GetValue(webView) as CoreWebView2Controller;
            }
        }

        return null;
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
#endif
