namespace Celbridge.Secrets;

/// <summary>
/// Default ISecretProvider backed by an in-memory name-to-getter map.
/// Each getter is a zero-arg closure invoked on resolve, so values read from
/// a rotating source are always current.
/// </summary>
public sealed class SecretProvider : ISecretProvider
{
    private readonly Dictionary<string, Func<string>> _valueGetters;

    public SecretProvider(IDictionary<string, Func<string>> valueGetters)
    {
        _valueGetters = new Dictionary<string, Func<string>>(valueGetters, StringComparer.Ordinal);
    }

    public string? TryResolve(string name)
    {
        if (_valueGetters.TryGetValue(name, out var getter))
        {
            return getter();
        }

        return null;
    }
}
