using Celbridge.Settings.Services;

namespace Celbridge.Tests.Settings;

/// <summary>
/// In-memory credential store for tests. Round-trips the secret bytes without encryption. Presence and
/// delete operate on the dictionary. The Available and FailRetrieve flags simulate an unavailable store and
/// a read failure.
/// </summary>
public sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, byte[]> _store = new();

    public bool Available { get; set; } = true;

    public bool FailRetrieve { get; set; }

    public bool IsAvailable => Available;

    public Result StoreCredential(string key, byte[] secret)
    {
        _store[key] = secret;

        return Result.Ok();
    }

    public Result<byte[]> RetrieveCredential(string key)
    {
        if (FailRetrieve)
        {
            return Result<byte[]>.Fail("Simulated retrieve failure");
        }

        if (!_store.TryGetValue(key, out var secret))
        {
            return Result<byte[]>.Fail($"No credential is stored for '{key}'");
        }

        return secret;
    }

    public bool ContainsCredential(string key)
    {
        return _store.ContainsKey(key);
    }

    public Result DeleteCredential(string key)
    {
        _store.Remove(key);

        return Result.Ok();
    }
}
