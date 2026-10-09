using System.Runtime.CompilerServices;
using Celbridge.Logging;

namespace Celbridge.WebHost;

internal sealed class WebSurfaceLog : IWebSurfaceLog
{
    private readonly ILogger<WebSurfaceLog> _logger;
    private readonly TimeProvider _timeProvider;

    // Each view's count of entries in its current window. A page in a render loop can report on every frame, so
    // the log needs a ceiling. The weak keys let an entry go with its view, and a rename keeps the view's count.
    private readonly ConditionalWeakTable<IWebView, SurfaceRate> _rates = new();
    private readonly object _ratesLock = new();

    private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(10);
    private const int MaxEntriesPerWindow = 50;

    // Long enough for a stack trace, short enough that a page cannot bloat the log with one entry.
    private const int MaxMessageLength = 2000;

    public WebSurfaceLog(ILogger<WebSurfaceLog> logger)
        : this(logger, TimeProvider.System)
    {
    }

    internal WebSurfaceLog(ILogger<WebSurfaceLog> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void Write(IWebView view, string? level, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var surfaceName = view.Resource.ToString();

        var allowance = TakeAllowance(view);
        if (allowance == RateAllowance.Denied)
        {
            return;
        }

        if (allowance == RateAllowance.LastBeforeLimit)
        {
            _logger.LogWarning(
                "Web surface {Surface} exceeded its log rate limit, so further entries are dropped for now",
                surfaceName);
            return;
        }

        var text = message.Length > MaxMessageLength
            ? string.Concat(message.AsSpan(0, MaxMessageLength), "...")
            : message;

        // The message is page-authored, so it is a log argument rather than part of the template.
        switch (level?.ToLowerInvariant())
        {
            case "error":
                _logger.LogError("Web surface {Surface}: {Message}", surfaceName, text);
                break;

            case "warn":
            case "warning":
                _logger.LogWarning("Web surface {Surface}: {Message}", surfaceName, text);
                break;

            case "info":
                _logger.LogInformation("Web surface {Surface}: {Message}", surfaceName, text);
                break;

            default:
                _logger.LogDebug("Web surface {Surface}: {Message}", surfaceName, text);
                break;
        }
    }

    private RateAllowance TakeAllowance(IWebView view)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_ratesLock)
        {
            var rate = _rates.GetValue(view, _ => new SurfaceRate { WindowStart = now });
            if (now - rate.WindowStart >= RateWindow)
            {
                rate.WindowStart = now;
                rate.Count = 0;
            }

            rate.Count++;

            if (rate.Count < MaxEntriesPerWindow)
            {
                return RateAllowance.Allowed;
            }

            return rate.Count == MaxEntriesPerWindow ? RateAllowance.LastBeforeLimit : RateAllowance.Denied;
        }
    }

    private sealed class SurfaceRate
    {
        public DateTimeOffset WindowStart;
        public int Count;
    }

    private enum RateAllowance
    {
        Allowed,
        LastBeforeLimit,
        Denied
    }
}
