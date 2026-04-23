namespace Celbridge.Host;

/// <summary>
/// Matches tool aliases against requires_tools glob patterns.
/// Supports literal aliases, namespace wildcards ("foo.*"), and "*".
/// </summary>
public static class ToolAllowlist
{
    public static bool Matches(string alias, string pattern)
    {
        if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        if (pattern == "*")
        {
            return true;
        }

        if (pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = pattern.Substring(0, pattern.Length - 1);
            return alias.StartsWith(prefix, StringComparison.Ordinal);
        }

        return string.Equals(alias, pattern, StringComparison.Ordinal);
    }

    public static bool IsAllowed(string alias, IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return false;
        }

        foreach (var pattern in patterns)
        {
            if (Matches(alias, pattern))
            {
                return true;
            }
        }

        return false;
    }
}
