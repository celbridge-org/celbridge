using Celbridge.Settings;

namespace Celbridge.Tests.Settings;

/// <summary>
/// In-memory ISettingsStore test double, standing in for both the Application and
/// Workspace stores. Composes the shared KeyValueStore so it serializes values
/// exactly as the production stores do; FlushAsync is a no-op since nothing is
/// persisted to disk.
/// </summary>
public sealed class FakeSettingsStore : ISettingsStore
{
    private readonly KeyValueStore _store = new();

    public bool TryGetValue<T>(string key, out T value) where T : notnull
    {
        return _store.TryGetValue(key, out value);
    }

    public void SetValue<T>(string key, T value) where T : notnull
    {
        _store.SetValue(key, value);
    }

    public bool ContainsKey(string key)
    {
        return _store.ContainsKey(key);
    }

    public void RemoveValue(string key)
    {
        _store.Remove(key);
    }

    public Task<Result> FlushAsync()
    {
        return Task.FromResult(Result.Ok());
    }
}
