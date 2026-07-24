using System.Globalization;

namespace Celbridge.Utilities;

/// <summary>
/// The package and version named by the newest HISTORY.md entry, parsed from its "name@version" heading.
/// </summary>
public sealed record InstalledPackageReference(string Name, int Version);

/// <summary>
/// Reads the installed version recorded in a package's generated HISTORY.md changelog.
/// </summary>
public static class PackageHistoryReader
{
    /// <summary>
    /// Reads the installed version from a HISTORY.md body. Returns null when the file has no parseable
    /// version heading (e.g. a hand-authored file).
    /// </summary>
    public static int? TryReadInstalledVersion(string historyMarkdown)
    {
        return TryReadInstalledReference(historyMarkdown)?.Version;
    }

    /// <summary>
    /// Reads the package and version named by the newest HISTORY.md entry, parsed from its "name@version"
    /// heading on the first non-empty line. Returns null when there is no parseable heading.
    /// </summary>
    public static InstalledPackageReference? TryReadInstalledReference(string historyMarkdown)
    {
        if (string.IsNullOrEmpty(historyMarkdown))
        {
            return null;
        }

        using var reader = new StringReader(historyMarkdown);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // The first non-empty line must be the newest entry's "name@version" heading.
            var headingText = trimmed.TrimStart('#').Trim();

            // Drop any trailing note after the token.
            var spaceIndex = headingText.IndexOf(' ');
            if (spaceIndex > 0)
            {
                headingText = headingText.Substring(0, spaceIndex);
            }

            var atIndex = headingText.LastIndexOf('@');
            if (atIndex <= 0
                || atIndex + 1 >= headingText.Length)
            {
                return null;
            }

            var name = headingText.Substring(0, atIndex);
            var versionText = headingText.Substring(atIndex + 1);

            if (int.TryParse(versionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
                && version > 0)
            {
                return new InstalledPackageReference(name, version);
            }

            return null;
        }

        return null;
    }
}
