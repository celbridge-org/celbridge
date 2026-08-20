using Celbridge.Logging;

namespace Celbridge.WebHost;

internal sealed class WebSurfaceLog : IWebSurfaceLog
{
    private readonly ILogger<WebSurfaceLog> _logger;
    private readonly TimeProvider _timeProvider;

    // Per surface, how many entries have been written in the window that started at WindowStart. A page in a
    // render loop can report on every frame, so the log (and the disk it lands on) needs a ceiling.
    private readonly Dictionary<string, SurfaceRate> _rates = new(StringComparer.Ordinal);

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

    public void Write(string surfaceName, string? level, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var allowance = TakeAllowance(surfaceName);
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

    private RateAllowance TakeAllowance(string surfaceName)
    {
        var now = _timeProvider.GetUtcNow();

        if (!_rates.TryGetValue(surfaceName, out var rate)
            || now - rate.WindowStart >= RateWindow)
        {
            _rates[surfaceName] = new SurfaceRate(now, 1);
            return RateAllowance.Allowed;
        }

        var count = rate.Count + 1;
        _rates[surfaceName] = rate with { Count = count };

        if (count < MaxEntriesPerWindow)
        {
            return RateAllowance.Allowed;
        }

        return count == MaxEntriesPerWindow ? RateAllowance.LastBeforeLimit : RateAllowance.Denied;
    }

    private sealed record SurfaceRate(DateTimeOffset WindowStart, int Count);

    private enum RateAllowance
    {
        Allowed,
        LastBeforeLimit,
        Denied
    }
}
