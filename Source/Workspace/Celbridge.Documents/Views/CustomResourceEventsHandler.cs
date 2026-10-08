using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Utilities;

namespace Celbridge.Documents.Views;

/// <summary>
/// Handles IHostResources RPC methods for a custom editor: relays the project's resource messages to a page
/// that has subscribed, filtered by its patterns.
///
/// The watcher reports one logical write as several events (an atomic save arrives as Created and Changed, or
/// as Deleted and Created), and an editor auto-saves on a timer while the user types. Created, Changed and
/// Deleted are therefore collapsed per resource until it has been quiet for the debounce period, so a page
/// sees one notification per settled change. A rename is reported as soon as it happens, after anything
/// still pending for either of its keys.
/// </summary>
public sealed class CustomResourceEventsHandler : IHostResources, IDisposable
{
    // Long enough to absorb the burst one save produces, short enough that a change still feels immediate.
    // Matches the console trigger debounce.
    private const int DebounceMilliseconds = 300;

    private readonly IMessengerService _messengerService;
    private readonly ILogger _logger;
    private readonly Func<ResourceChangeNotification, Task> _notify;
    private readonly Func<int, Task> _delayAsync;

    private readonly object _lock = new();

    // Null while the page has no subscription. An empty list subscribes to everything.
    private IReadOnlyList<ResourcePathMatcher>? _matchers;

    // The change waiting to be reported for each resource, and the request number that will report it. A wait
    // that wakes to find a higher number has been superseded by a later event for the same resource.
    private readonly Dictionary<ResourceKey, PendingChange> _pending = new();
    private int _requestCounter;
    private bool _registered;
    private bool _disposed;

    private sealed record PendingChange(string Kind, int RequestNumber);

    /// <summary>
    /// The notify callback sends one resources/changed notification to the page. The delay hook exists so
    /// tests can drive the debounce without a real clock.
    /// </summary>
    public CustomResourceEventsHandler(
        IMessengerService messengerService,
        ILogger logger,
        Func<ResourceChangeNotification, Task> notify,
        Func<int, Task>? delayAsync = null)
    {
        _messengerService = messengerService;
        _logger = logger;
        _notify = notify;
        _delayAsync = delayAsync ?? (milliseconds => Task.Delay(milliseconds));
    }

    public void Subscribe(IReadOnlyList<string>? patterns = null)
    {
        var matchers = new List<ResourcePathMatcher>();
        if (patterns is not null)
        {
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                try
                {
                    matchers.Add(ResourcePathMatcher.Compile(pattern.Trim()));
                }
                catch (Exception exception)
                {
                    throw new ArgumentException($"Invalid resource pattern '{pattern}': {exception.Message}", nameof(patterns));
                }
            }
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _matchers = matchers;
            _pending.Clear();

            if (!_registered)
            {
                _messengerService.Register<ResourceCreatedMessage>(this, (_, message) => OnContentEvent(message.Resource, ResourceChangeKinds.Created));
                _messengerService.Register<ResourceChangedMessage>(this, (_, message) => OnContentEvent(message.Resource, ResourceChangeKinds.Changed));
                _messengerService.Register<ResourceDeletedMessage>(this, (_, message) => OnContentEvent(message.Resource, ResourceChangeKinds.Deleted));
                _messengerService.Register<ResourceRenamedMessage>(this, (_, message) => OnRenamed(message.OldResource, message.NewResource));
                _registered = true;
            }
        }
    }

    public void Unsubscribe()
    {
        lock (_lock)
        {
            _matchers = null;
            _pending.Clear();
            UnregisterMessages();
        }
    }

    private void OnContentEvent(ResourceKey resource, string kind)
    {
        int requestNumber;
        lock (_lock)
        {
            if (!IsWatched(resource))
            {
                return;
            }

            _pending.TryGetValue(resource, out var existing);
            var mergedKind = MergeKinds(existing?.Kind, kind);

            requestNumber = ++_requestCounter;
            if (mergedKind is null)
            {
                // Created and deleted inside one quiet period (a temporary file): nothing settled to report.
                // The entry is kept as a tombstone so the wait already running for it reports nothing.
                _pending[resource] = new PendingChange(string.Empty, requestNumber);
            }
            else
            {
                _pending[resource] = new PendingChange(mergedKind, requestNumber);
            }
        }

        _ = ReportAfterQuietAsync(resource, requestNumber);
    }

    /// <summary>
    /// Folds a new event into the change already pending for a resource. Returns null when the two cancel out.
    /// The first event of a burst decides what it was, except that a file deleted and recreated was changed,
    /// and a file that ends the burst deleted was deleted.
    /// </summary>
    public static string? MergeKinds(string? pendingKind, string newKind)
    {
        if (string.IsNullOrEmpty(pendingKind))
        {
            return newKind;
        }

        if (newKind == ResourceChangeKinds.Deleted)
        {
            return pendingKind == ResourceChangeKinds.Created ? null : ResourceChangeKinds.Deleted;
        }

        if (pendingKind == ResourceChangeKinds.Deleted)
        {
            return ResourceChangeKinds.Changed;
        }

        return pendingKind;
    }

    private async Task ReportAfterQuietAsync(ResourceKey resource, int requestNumber)
    {
        try
        {
            await _delayAsync(DebounceMilliseconds);

            PendingChange? change;
            lock (_lock)
            {
                if (!_pending.TryGetValue(resource, out change) ||
                    change.RequestNumber != requestNumber)
                {
                    return;
                }

                _pending.Remove(resource);
            }

            if (string.IsNullOrEmpty(change.Kind))
            {
                return;
            }

            await _notify(new ResourceChangeNotification(change.Kind, resource.ToString()));
        }
        catch (Exception exception)
        {
            // Runs on a background task with nothing above it to observe a fault, and a page that has gone
            // away mid-wait is ordinary.
            _logger.LogDebug(exception, "Failed to report a resource change to a custom editor");
        }
    }

    private void OnRenamed(ResourceKey oldResource, ResourceKey newResource)
    {
        var flushed = new List<ResourceChangeNotification>();
        lock (_lock)
        {
            var oldWatched = IsWatched(oldResource);
            var newWatched = IsWatched(newResource);
            if (!oldWatched && !newWatched)
            {
                return;
            }

            // Report what was waiting for either key first, so the page sees the changes in the order they
            // happened. Removing the entries supersedes the waits already running for them.
            foreach (var key in new[] { oldResource, newResource })
            {
                if (_pending.Remove(key, out var change) &&
                    !string.IsNullOrEmpty(change.Kind))
                {
                    flushed.Add(new ResourceChangeNotification(change.Kind, key.ToString()));
                }
            }

            if (oldWatched && newWatched)
            {
                flushed.Add(new ResourceChangeNotification(ResourceChangeKinds.Renamed, newResource.ToString(), oldResource.ToString()));
            }
            else if (newWatched)
            {
                // A file moved in from outside the patterns (an atomic save renaming a temporary file over
                // the real one) arrives as far as the page is concerned.
                flushed.Add(new ResourceChangeNotification(ResourceChangeKinds.Created, newResource.ToString()));
            }
            else
            {
                flushed.Add(new ResourceChangeNotification(ResourceChangeKinds.Deleted, oldResource.ToString()));
            }
        }

        _ = SendAllAsync(flushed);
    }

    private async Task SendAllAsync(IReadOnlyList<ResourceChangeNotification> notifications)
    {
        try
        {
            foreach (var notification in notifications)
            {
                await _notify(notification);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to report a resource rename to a custom editor");
        }
    }

    // Caller holds the lock. Only files in the project tree are reported: the hidden roots (temp:, logs:,
    // utils:) hold the application's and the editors' own state, not the user's work.
    private bool IsWatched(ResourceKey resource)
    {
        if (_disposed ||
            _matchers is null ||
            resource.IsEmpty ||
            resource.Root != ResourceKey.DefaultRoot)
        {
            return false;
        }

        if (_matchers.Count == 0)
        {
            return true;
        }

        foreach (var matcher in _matchers)
        {
            if (matcher.IsMatch(resource.Path, isFolder: false))
            {
                return true;
            }
        }

        return false;
    }

    // Caller holds the lock.
    private void UnregisterMessages()
    {
        if (!_registered)
        {
            return;
        }

        _messengerService.UnregisterAll(this);
        _registered = false;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _matchers = null;
            _pending.Clear();
            UnregisterMessages();
        }
    }
}
