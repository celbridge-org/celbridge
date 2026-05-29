namespace Celbridge.Documents.Services;

/// <summary>
/// Holds reload hints keyed by ResourceKey with a short TTL. Used by DocumentsService
/// to bridge a command that wrote a file and the watcher-driven reload that follows.
/// </summary>
public sealed class ReloadHintStore
{
    private readonly Dictionary<ResourceKey, ReloadHintEntry> _entries = new();
    private readonly TimeSpan _ttl;
    private readonly Func<DateTime> _nowUtc;

    public ReloadHintStore(TimeSpan ttl, Func<DateTime>? nowUtc = null)
    {
        _ttl = ttl;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    public void Register(ResourceKey fileResource, ReloadHint hint)
    {
        var entry = new ReloadHintEntry(hint, _nowUtc() + _ttl);
        _entries[fileResource] = entry;
    }

    public ReloadHint Consume(ResourceKey fileResource)
    {
        if (!_entries.TryGetValue(fileResource, out var entry))
        {
            return ReloadHint.PreserveViewState;
        }

        _entries.Remove(fileResource);

        if (entry.ExpiresUtc < _nowUtc())
        {
            return ReloadHint.PreserveViewState;
        }

        return entry.Hint;
    }

    private readonly record struct ReloadHintEntry(ReloadHint Hint, DateTime ExpiresUtc);
}
