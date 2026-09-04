using System.Text;
using System.Text.Json;
using Celbridge.Logging;
using Celbridge.Settings;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Services;

/// <summary>
/// The surface a diagnostic line describes: the name it is logged under, the control behind it, and whether
/// an empty document on it is a failed load rather than a page that is legitimately blank.
/// </summary>
public sealed record WebViewSurface(string Name, WebView2? WebView, bool TreatEmptyDocumentAsFailure);

/// <summary>
/// What the probe of a completed navigation found: the host's reading of the page's report, and whether it
/// describes a document the response left empty.
/// </summary>
public sealed record WebViewContentProbe(string Reading, bool IsEmpty);

/// <summary>
/// What reading a page's probe report produced.
/// </summary>
internal enum ProbeOutcome
{
    /// <summary>
    /// The report could not be read, or describes a page that says nothing about what a response delivered.
    /// </summary>
    NoVerdict,

    /// <summary>
    /// The document has not finished parsing, so what it holds now is not what the response delivered.
    /// </summary>
    StillParsing,

    /// <summary>
    /// The report was read.
    /// </summary>
    Read
}

/// <summary>
/// Diagnostics for a hosted page that loads blank: the surface a load runs against, read at every navigation
/// and attach event, and a probe of what a completed navigation actually produced.
/// </summary>
public sealed class WebViewLoadDiagnostics
{
    // Reports the shape of the document the navigation actually produced. A load that delivers no body still
    // reaches its completion as a success, so the document arrives committed, focusable and running its
    // document-start scripts with nothing in it. The WebView has no signal for that.
    //
    // The character count is taken only for a document with no elements: serialising a real page's DOM to
    // measure a string the host compares against a small constant costs far more than the reading is worth.
    private const string ContentProbeScript = """
        (function () {
            var root = document.documentElement;
            var entry = performance.getEntriesByType('navigation')[0];
            var headCount = document.head ? document.head.childElementCount : 0;
            var bodyCount = document.body ? document.body.childElementCount : 0;

            return JSON.stringify({
                url: location.href,
                readyState: document.readyState,
                htmlLength: (root && headCount === 0 && bodyCount === 0) ? root.outerHTML.length : -1,
                headChildCount: headCount,
                bodyChildCount: bodyCount,
                encodedBodySize: entry ? entry.encodedBodySize : -1,
                transferSize: entry ? entry.transferSize : -1,
                redirectCount: entry ? entry.redirectCount : -1,
                protocol: entry ? entry.nextHopProtocol : '',
                responseStart: entry ? Math.round(entry.responseStart) : -1,
                responseEnd: entry ? Math.round(entry.responseEnd) : -1,
                hidden: document.hidden,
                clientWidth: root ? root.clientWidth : 0,
                clientHeight: root ? root.clientHeight : 0
            });
        })()
        """;

    // A document with no content of its own still serialises the html, head and body the parser implies,
    // which is a little over 40 characters. Anything longer carries something the response put there.
    private const int EmptyDocumentHtmlLength = 128;

    // The page chooses these two values, so they are capped before they reach the log.
    private const int MaxPageTextLength = 200;

    // How long a document that is still parsing is given before the one further look it gets.
    private static readonly TimeSpan StillParsingRetryDelay = TimeSpan.FromSeconds(2);

    private readonly IWebViewAdapter _webViewAdapter;
    private readonly IFeatureFlags _featureFlags;
    private readonly ILogger _logger;

    public WebViewLoadDiagnostics(IWebViewAdapter webViewAdapter, IFeatureFlags featureFlags, ILogger logger)
    {
        _webViewAdapter = webViewAdapter;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    // Read per call rather than cached, because a project's overrides are applied when it loads and can
    // change while the app is running. A page that loads blank is reported either way: what this gates is
    // the timeline around it, which is the bulk of the volume.
    private bool IsNarrationEnabled => _featureFlags.IsEnabled(FeatureFlagConstants.WebViewLoadDiagnostics);

    /// <summary>
    /// The surface a load runs against, for the log: the control's tree and layout state, and the native
    /// state the adapter can see behind it.
    /// </summary>
    public string DescribeSurface(WebViewSurface surface)
    {
        var webView = surface.WebView;
        if (webView is null)
        {
            return "webview=none";
        }

        var native = webView.CoreWebView2 is CoreWebView2 coreWebView
            ? _webViewAdapter.DescribeNativeSurface(coreWebView)
            : "native=none";

        return $"loaded={webView.IsLoaded} size={webView.ActualWidth:F0}x{webView.ActualHeight:F0} "
            + $"xamlRoot={webView.XamlRoot is not null} {native}".TrimEnd();
    }

    /// <summary>
    /// Logs a navigation reaching one of its stages.
    /// </summary>
    public void LogNavigation(string moment, WebViewSurface surface, string? url)
    {
        if (!IsNarrationEnabled)
        {
            return;
        }

        _logger.LogDebug("{Moment} for {Resource} at {Url} ({Surface})", moment, surface.Name, url, DescribeSurface(surface));
    }

    /// <summary>
    /// Logs a navigation that did not arrive. A navigation the host itself declined is reported as the
    /// ordinary outcome it is, so an enforced navigation policy does not read as a broken page.
    /// </summary>
    public void LogNavigationFailed(WebViewSurface surface, string? url, CoreWebView2WebErrorStatus status)
    {
        if (status == CoreWebView2WebErrorStatus.OperationCanceled)
        {
            if (IsNarrationEnabled)
            {
                _logger.LogDebug("Navigation cancelled for {Resource} at {Url} ({Surface})", surface.Name, url, DescribeSurface(surface));
            }

            return;
        }

        _logger.LogWarning(
            "Navigation failed for {Resource} at {Url} with status {Status} ({Surface})",
            surface.Name,
            url,
            status,
            DescribeSurface(surface));
    }

    /// <summary>
    /// Logs the surface at a lifecycle moment together with the page's scroll offset, read through the page
    /// because the host cannot see it: whether the offset survives a detach says whether a tab switch is
    /// what resets it.
    /// </summary>
    public async Task LogSurfaceAsync(string moment, WebViewSurface surface)
    {
        if (!IsNarrationEnabled)
        {
            return;
        }

        var described = DescribeSurface(surface);

        var scrollY = "n/a";
        if (surface.WebView?.CoreWebView2 is CoreWebView2 coreWebView)
        {
            try
            {
                scrollY = await _webViewAdapter.EvalAsync(coreWebView, "window.scrollY");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read the scroll offset of {Resource}", surface.Name);
            }
        }

        _logger.LogDebug("{Moment} for {Resource} ({Surface}) scrollY={ScrollY}", moment, surface.Name, described, scrollY);
    }

    /// <summary>
    /// Probes the document the page currently holds. Null when the page gave no verdict: the probe could not
    /// run, the document is still the blank page a load starts from, or it is still parsing.
    /// </summary>
    public async Task<WebViewContentProbe?> ProbeAsync(WebViewSurface surface)
    {
        var (outcome, probe) = await ReadProbeAsync(surface);
        if (outcome != ProbeOutcome.StillParsing)
        {
            return probe;
        }

        // A load that started while its view was detached can reach the attach probe mid-parse, and on the
        // Skia heads no completion event need follow to prompt another look, so the one chance to see what
        // the response delivered would otherwise be spent on a document that had not received it yet.
        if (IsNarrationEnabled)
        {
            _logger.LogDebug("The document of {Resource} was still parsing, so it is probed once more", surface.Name);
        }

        await Task.Delay(StillParsingRetryDelay);

        var (_, settledProbe) = await ReadProbeAsync(surface);
        return settledProbe;
    }

    private async Task<(ProbeOutcome Outcome, WebViewContentProbe? Probe)> ReadProbeAsync(WebViewSurface surface)
    {
        if (surface.WebView?.CoreWebView2 is not CoreWebView2 coreWebView)
        {
            return (ProbeOutcome.NoVerdict, null);
        }

        try
        {
            var result = await _webViewAdapter.EvalAsync(coreWebView, ContentProbeScript);
            var outcome = ReadContentProbe(result, out var probe);

            return (outcome, outcome == ProbeOutcome.Read ? probe : null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not probe the content loaded by {Resource}", surface.Name);
            return (ProbeOutcome.NoVerdict, null);
        }
    }

    /// <summary>
    /// Writes the probe to the log: a warning for an empty document on a surface that treats one as a failed
    /// load, a debug line otherwise.
    /// </summary>
    public void LogProbe(WebViewSurface surface, string url, WebViewContentProbe probe)
    {
        if (probe.IsEmpty && surface.TreatEmptyDocumentAsFailure)
        {
            _logger.LogWarning(
                "Navigation for {Resource} completed at {Url} but produced an empty document: {Probe} ({Surface})",
                surface.Name,
                url,
                probe.Reading,
                DescribeSurface(surface));
            return;
        }

        if (!IsNarrationEnabled)
        {
            return;
        }

        _logger.LogDebug("Content probe for {Resource} at {Url}: {Probe} ({Surface})", surface.Name, url, probe.Reading, DescribeSurface(surface));
    }

    // Reads the page's report into a reading the host built itself. The report is page-authored, so nothing
    // from it reaches the log unvalidated: numbers are logged as numbers, and the two strings the page
    // chooses are capped and stripped of the line breaks that would let a page forge log entries. A report
    // that cannot be read is no verdict: the page keeps the success it was given.
    internal static ProbeOutcome ReadContentProbe(string? result, out WebViewContentProbe probe)
    {
        probe = new WebViewContentProbe(string.Empty, false);

        var json = WebMessageEnvelope.UnwrapPageJson(result);
        if (string.IsNullOrEmpty(json))
        {
            return ProbeOutcome.NoVerdict;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ProbeOutcome.NoVerdict;
            }

            if (!TryReadNumber(root, "htmlLength", out var htmlLength)
                || !TryReadNumber(root, "headChildCount", out var headChildCount)
                || !TryReadNumber(root, "bodyChildCount", out var bodyChildCount))
            {
                return ProbeOutcome.NoVerdict;
            }

            // The blank page a load starts from is empty by design, so it says nothing about a response.
            var url = ReadPageText(root, "url");
            if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                return ProbeOutcome.NoVerdict;
            }

            // A document still parsing may not hold what the response delivered yet, so it is worth a
            // second look rather than a verdict.
            if (ReadPageText(root, "readyState") != "complete")
            {
                return ProbeOutcome.StillParsing;
            }

            var reading = new StringBuilder()
                .Append("url=").Append(url)
                .Append(" html=").Append(htmlLength)
                .Append(" head=").Append(headChildCount)
                .Append(" body=").Append(bodyChildCount)
                .Append(" encoded=").Append(ReadNumber(root, "encodedBodySize"))
                .Append(" transfer=").Append(ReadNumber(root, "transferSize"))
                .Append(" redirects=").Append(ReadNumber(root, "redirectCount"))
                .Append(" protocol=").Append(ReadPageText(root, "protocol"))
                .Append(" responseStart=").Append(ReadNumber(root, "responseStart"))
                .Append(" responseEnd=").Append(ReadNumber(root, "responseEnd"))
                .Append(" hidden=").Append(ReadBool(root, "hidden"))
                .Append(" client=").Append(ReadNumber(root, "clientWidth"))
                .Append('x').Append(ReadNumber(root, "clientHeight"))
                .ToString();

            // htmlLength is -1 for a document the page did not measure, which it only skips when the
            // document has elements in it.
            var isEmpty = headChildCount == 0
                && bodyChildCount == 0
                && htmlLength >= 0
                && htmlLength <= EmptyDocumentHtmlLength;

            probe = new WebViewContentProbe(reading, isEmpty);
            return ProbeOutcome.Read;
        }
        catch (JsonException)
        {
            return ProbeOutcome.NoVerdict;
        }

        static string ReadPageText(JsonElement element, string name)
        {
            var text = WebMessageEnvelope.ReadString(element, name) ?? string.Empty;
            if (text.Length > MaxPageTextLength)
            {
                text = string.Concat(text.AsSpan(0, MaxPageTextLength), "...");
            }

            return text.ReplaceLineEndings(" ");
        }

        static long ReadNumber(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt64(out var value)
                ? value
                : -1;
        }

        static bool ReadBool(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.True;
        }

        static bool TryReadNumber(JsonElement element, string name, out int value)
        {
            value = 0;

            return element.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out value);
        }
    }
}
