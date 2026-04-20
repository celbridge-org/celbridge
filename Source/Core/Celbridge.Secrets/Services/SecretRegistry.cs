namespace Celbridge.Secrets;

/// <summary>
/// Default ISecretRegistry that queries each provider in order until one resolves.
/// </summary>
public sealed class SecretRegistry : ISecretRegistry
{
    private readonly IReadOnlyList<ISecretProvider> _providers;

    public SecretRegistry(IEnumerable<ISecretProvider> providers)
    {
        _providers = providers.ToList().AsReadOnly();
    }

    public string? TryResolve(string name)
    {
        foreach (var provider in _providers)
        {
            var value = provider.TryResolve(name);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    public Result<IReadOnlyDictionary<string, string>> ResolveAll(IEnumerable<string> names)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (resolved.ContainsKey(name))
            {
                continue;
            }

            var value = TryResolve(name);
            if (value is null)
            {
                missing.Add(name);
                continue;
            }

            resolved[name] = value;
        }

        if (missing.Count > 0)
        {
            var missingList = string.Join(", ", missing);
            return Result.Fail($"Secret(s) not registered: {missingList}");
        }

        return Result<IReadOnlyDictionary<string, string>>.Ok(resolved);
    }
}
