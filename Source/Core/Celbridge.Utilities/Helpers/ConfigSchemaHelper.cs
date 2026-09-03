namespace Celbridge.Utilities;

/// <summary>
/// Checks the keys a config or manifest file declares against the keys its format defines. Every format
/// reports what it finds in its own way, so this reports the unknown keys rather than acting on them.
/// </summary>
public static class ConfigSchemaHelper
{
    /// <summary>
    /// Returns the keys the format does not define, in the order they were declared. Empty when every
    /// key is known. Keys are matched exactly, which is how both TOML and JSON compare them, so the
    /// known-key set is expected to use an ordinal comparer.
    /// </summary>
    public static IReadOnlyList<string> FindUnknownKeys(IEnumerable<string> declaredKeys, IReadOnlySet<string> knownKeys)
    {
        List<string>? unknownKeys = null;

        foreach (var declaredKey in declaredKeys)
        {
            if (knownKeys.Contains(declaredKey))
            {
                continue;
            }

            unknownKeys ??= new List<string>();
            unknownKeys.Add(declaredKey);
        }

        if (unknownKeys is null)
        {
            return Array.Empty<string>();
        }

        return unknownKeys;
    }
}
