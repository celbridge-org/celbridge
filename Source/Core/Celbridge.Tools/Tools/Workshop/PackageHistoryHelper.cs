using System.Globalization;
using System.Text;
using Celbridge.Workshop;
using StringReader = System.IO.StringReader;

namespace Celbridge.Tools;

/// <summary>
/// The package and workshop version named by the newest HISTORY.md entry, parsed from its "name@version" heading.
/// </summary>
internal sealed record InstalledPackageReference(string Name, int WorkshopVersion);

/// <summary>
/// Formats and parses the generated HISTORY.md changelog written beside a package manifest.
/// </summary>
internal static class PackageHistoryHelper
{
    // Marker text rendered in place of a deleted workshop version's summary.
    private const string DeletedVersionSummary = "[package_deleted]";

    // Length of the truncated content fingerprint, matching the git short-hash
    // convention. The full hash stays authoritative in workshop_get_package_info.
    private const int ShortHashLength = 12;

    /// <summary>
    /// Builds the HISTORY.md changelog from the package's workshop versions, covering
    /// every workshop version up to and including the installed one, newest first. Fails
    /// when no workshop version is at or below the installed one, since an empty changelog
    /// is not a meaningful install record.
    /// </summary>
    public static Result<string> Format(string packageName, IReadOnlyList<RemoteWorkshopVersion> workshopVersions, int installedWorkshopVersion)
    {
        var orderedWorkshopVersions = workshopVersions
            .Where(workshopVersion => workshopVersion.WorkshopVersion <= installedWorkshopVersion)
            .OrderByDescending(workshopVersion => workshopVersion.WorkshopVersion)
            .ToList();

        if (orderedWorkshopVersions.Count == 0)
        {
            return Result.Fail($"Cannot build history for package '{packageName}': no workshop version at or below {installedWorkshopVersion}.");
        }

        var builder = new StringBuilder();
        foreach (var workshopVersion in orderedWorkshopVersions)
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
            builder.Append(workshopVersion.WorkshopVersion.ToString(CultureInfo.InvariantCulture));
            builder.Append("\r\n\r\n");

            AppendMetadataLine(builder, workshopVersion);

            // The body is the free-text summary, or the deleted marker. A deleted
            // workshop version keeps its heading and metadata but loses the summary,
            // so it reads as removed rather than as a gap in the numbering.
            string body;
            if (workshopVersion.Deleted)
            {
                body = DeletedVersionSummary;
            }
            else
            {
                var summary = workshopVersion.Summary?.Trim() ?? string.Empty;
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
    private static void AppendMetadataLine(StringBuilder builder, RemoteWorkshopVersion workshopVersion)
    {
        var fields = new List<string>();
        fields.Add($"time: {FormatTimestamp(workshopVersion.Date)}");

        var author = workshopVersion.Author?.Trim() ?? string.Empty;
        if (author.Length > 0)
        {
            fields.Add($"author: {author}");
        }

        var hash = TruncateHash(workshopVersion.ContentHash);
        if (hash.Length > 0)
        {
            fields.Add($"hash: {hash}");
        }

        if (workshopVersion.Deleted)
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
    // hash stays authoritative in workshop_get_package_info.
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
    /// Reads the package and workshop version named by the newest HISTORY.md entry, parsed from its
    /// "name@version" heading on the first non-empty line. Returns null when there is no parseable
    /// heading (e.g. a hand-authored file).
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
            var workshopVersionText = headingText.Substring(atIndex + 1);

            if (int.TryParse(workshopVersionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var workshopVersion)
                && workshopVersion > 0)
            {
                return new InstalledPackageReference(name, workshopVersion);
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Returns true when the source folder's install record is a stale base for
    /// publishing: a same-package workshop version older than the latest live workshop
    /// version, signalling another publish landed since this folder was installed.
    /// </summary>
    public static bool IsStaleBase(InstalledPackageReference? installed, string packageName, int latestLiveWorkshopVersion)
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

        return installed.WorkshopVersion < latestLiveWorkshopVersion;
    }
}
