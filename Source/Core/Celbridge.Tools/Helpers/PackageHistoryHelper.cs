using System.Globalization;
using System.IO;
using System.Text;

namespace Celbridge.Tools;

/// <summary>
/// The package and version named by the newest HISTORY.md entry, parsed from
/// its "name@version" heading.
/// </summary>
public sealed record InstalledPackageReference(string Name, int Version);

/// <summary>
/// Formats and parses the generated HISTORY.md changelog written beside a package manifest.
/// </summary>
internal static class PackageHistoryHelper
{
    // Marker text rendered in place of a deleted version's summary.
    private const string DeletedVersionSummary = "[package_deleted]";

    // Length of the truncated content fingerprint, matching the git short-hash
    // convention. The full hash stays authoritative in package_info.
    private const int ShortHashLength = 12;

    /// <summary>
    /// Builds the HISTORY.md changelog from the package's versions, covering
    /// every version up to and including the installed one, newest first. Fails
    /// when no version is at or below the installed one, since an empty changelog
    /// is not a meaningful install record.
    /// </summary>
    public static Result<string> Format(string packageName, IReadOnlyList<RemotePackageVersion> versions, int installedVersion)
    {
        var orderedVersions = versions
            .Where(packageVersion => packageVersion.Version <= installedVersion)
            .OrderByDescending(packageVersion => packageVersion.Version)
            .ToList();

        if (orderedVersions.Count == 0)
        {
            return Result.Fail($"Cannot build history for package '{packageName}': no version at or below {installedVersion}.");
        }

        var builder = new StringBuilder();
        foreach (var packageVersion in orderedVersions)
        {
            if (builder.Length > 0)
            {
                builder.Append("\r\n");
            }

            // The header carries the name@version token so a single entry is
            // self-describing and survives a rename when read standalone.
            builder.Append("# ");
            builder.Append(packageName);
            builder.Append('@');
            builder.Append(packageVersion.Version.ToString(CultureInfo.InvariantCulture));
            builder.Append("\r\n\r\n");

            AppendMetadataLine(builder, packageVersion);

            // The body is the free-text summary, or the deleted marker. A deleted
            // version keeps its heading and metadata but loses the summary, so the
            // version reads as removed rather than as a gap in the numbering.
            string body;
            if (packageVersion.Deleted)
            {
                body = DeletedVersionSummary;
            }
            else
            {
                var summary = packageVersion.Summary?.Trim() ?? string.Empty;
                body = summary.Replace("\r\n", "\n").Replace("\n", "\r\n");
            }

            if (body.Length > 0)
            {
                builder.Append("\r\n");
                builder.Append(body);
                builder.Append("\r\n");
            }
        }

        return builder.ToString();
    }

    // One compact bracketed line carrying the entry's fixed metadata fields, so a
    // grep hit or a quoted fragment returns the whole record in a single match.
    private static void AppendMetadataLine(StringBuilder builder, RemotePackageVersion packageVersion)
    {
        var fields = new List<string>();
        fields.Add($"time: {FormatTimestamp(packageVersion.Date)}");

        var author = packageVersion.Author?.Trim() ?? string.Empty;
        if (author.Length > 0)
        {
            fields.Add($"author: {author}");
        }

        var hash = TruncateHash(packageVersion.ContentHash);
        if (hash.Length > 0)
        {
            fields.Add($"hash: {hash}");
        }

        if (packageVersion.Deleted)
        {
            fields.Add("deleted: true");
        }

        builder.Append('[');
        builder.Append(string.Join(", ", fields));
        builder.Append("]\r\n");
    }

    // Renders the publish time as RFC 3339 / ISO 8601 in UTC with a Z suffix.
    // Date-only would not distinguish versions published minutes apart on the
    // same day, which must stay ordered. The workshop sends UTC timestamps, so
    // an unspecified kind is taken as UTC rather than shifted as local.
    private static string FormatTimestamp(DateTime date)
    {
        DateTime utc = date.Kind switch
        {
            DateTimeKind.Utc => date,
            DateTimeKind.Local => date.ToUniversalTime(),
            _ => DateTime.SpecifyKind(date, DateTimeKind.Utc)
        };

        return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    // Truncates the content hash to a short fingerprint, dropping any algorithm
    // prefix such as "sha256:". The short form is for cheap reasoning. The full
    // hash stays authoritative in package_info.
    private static string TruncateHash(string? contentHash)
    {
        var hash = contentHash?.Trim() ?? string.Empty;
        if (hash.Length == 0)
        {
            return string.Empty;
        }

        var colonIndex = hash.LastIndexOf(':');
        if (colonIndex >= 0
            && colonIndex + 1 < hash.Length)
        {
            hash = hash.Substring(colonIndex + 1);
        }

        return hash.Length <= ShortHashLength ? hash : hash.Substring(0, ShortHashLength);
    }

    /// <summary>
    /// Reads the installed version from a HISTORY.md body. Returns null when the
    /// file has no parseable version heading (e.g. a hand-authored file).
    /// </summary>
    public static int? TryReadInstalledVersion(string historyMarkdown)
    {
        return TryReadInstalledReference(historyMarkdown)?.Version;
    }

    /// <summary>
    /// Reads the package and version named by the newest HISTORY.md entry, parsed
    /// from its "name@version" heading on the first non-empty line. Returns null
    /// when there is no parseable heading.
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

    /// <summary>
    /// Returns true when the source folder's install record is a stale base for
    /// publishing: a same-package version older than the workshop's latest live
    /// version, signalling another publish landed since this folder was installed.
    /// </summary>
    public static bool IsStaleBase(InstalledPackageReference? installed, string packageName, int latestLiveVersion)
    {
        if (installed is null)
        {
            return false;
        }

        // Only same-package iteration is a lost-update risk. A different recorded
        // name is a rename or fork, not a stale base.
        var samePackage = string.Equals(installed.Name, packageName, StringComparison.Ordinal);
        if (!samePackage)
        {
            return false;
        }

        return installed.Version < latestLiveVersion;
    }
}
