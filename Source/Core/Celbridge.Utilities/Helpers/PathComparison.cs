namespace Celbridge.Utilities;

/// <summary>
/// Selects the case sensitivity used when matching absolute filesystem paths.
/// Windows (NTFS) and macOS (default APFS) are case-insensitive but
/// case-preserving, so path matching ignores case there. Linux filesystems are
/// case-sensitive.
/// </summary>
public static class PathComparison
{
    // Default APFS behaves like NTFS: case-insensitive but case-preserving. A
    // deliberately case-sensitive APFS volume is rare and not detected here, so
    // two paths differing solely by case are treated as one on such a volume.
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
