namespace Celbridge.Utilities;

/// <summary>
/// Compares environment variable names. Windows ignores case in these names, and macOS and Linux do not.
/// </summary>
public static class EnvironmentVariableNames
{
    public const string PathName = "PATH";

    public static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static StringComparer Comparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Whether the name is PATH, in any spelling this platform treats as PATH.
    /// </summary>
    public static bool IsPath(string name)
    {
        return string.Equals(name, PathName, Comparison);
    }
}
