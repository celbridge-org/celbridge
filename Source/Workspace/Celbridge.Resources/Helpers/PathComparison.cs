namespace Celbridge.Resources.Helpers;

/// <summary>
/// Selects the case sensitivity used when matching absolute filesystem paths
/// against resource root backing locations. Windows (NTFS) and macOS (default
/// APFS) are case-insensitive but case-preserving, so path prefix matching
/// ignores case there; Linux filesystems are case-sensitive.
/// </summary>
public static class PathComparison
{
    // Default APFS behaves like NTFS: case-insensitive but case-preserving. A
    // deliberately case-sensitive APFS volume is rare and not detected here; the
    // only consequence on such a volume is that two roots differing solely by
    // case would be treated as one, which never happens for real project roots.
    private static bool IsCaseInsensitiveFileSystem =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static StringComparison Comparison =>
        IsCaseInsensitiveFileSystem
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static StringComparer Comparer =>
        IsCaseInsensitiveFileSystem
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
