using System.Globalization;
using System.IO;
using System.Text;

namespace Celbridge.Tools;

/// <summary>
/// Formats and parses the generated HISTORY.md changelog written beside a package manifest.
/// </summary>
internal static class PackageHistoryFile
{
    /// <summary>
    /// Builds the HISTORY.md changelog from the package's versions, covering
    /// every version up to and including the installed one, newest first.
    /// </summary>
    public static string Format(IReadOnlyList<RemotePackageVersion> versions, int installedVersion)
    {
        var orderedVersions = versions
            .Where(packageVersion => packageVersion.Version <= installedVersion)
            .OrderByDescending(packageVersion => packageVersion.Version)
            .ToList();

        var builder = new StringBuilder();
        foreach (var packageVersion in orderedVersions)
        {
            if (builder.Length > 0)
            {
                builder.Append("\r\n");
            }

            builder.Append("# ");
            builder.Append(packageVersion.Version.ToString(CultureInfo.InvariantCulture));
            if (packageVersion.Tombstoned)
            {
                builder.Append(" (tombstoned)");
            }
            builder.Append("\r\n\r\n");

            var date = packageVersion.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var author = packageVersion.Author?.Trim() ?? string.Empty;
            if (author.Length > 0)
            {
                builder.Append($"Published by {author} on {date}.\r\n");
            }
            else
            {
                builder.Append($"Published on {date}.\r\n");
            }

            // The content hash fingerprints the published version, letting a
            // vendored copy be verified against the workshop's record. It is
            // self-describing, so it stands alone without a label.
            var contentHash = packageVersion.ContentHash?.Trim() ?? string.Empty;
            if (contentHash.Length > 0)
            {
                builder.Append(contentHash);
                builder.Append("\r\n");
            }

            var summary = packageVersion.Summary?.Trim() ?? string.Empty;
            if (summary.Length > 0)
            {
                var normalizedSummary = summary.Replace("\r\n", "\n").Replace("\n", "\r\n");
                builder.Append("\r\n");
                builder.Append(normalizedSummary);
                builder.Append("\r\n");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads the installed version from a HISTORY.md body: the first non-empty
    /// line is the newest version's heading ("# 23"). Returns null when the
    /// file has no parseable version heading (e.g. a hand-authored file).
    /// </summary>
    public static int? TryReadInstalledVersion(string historyMarkdown)
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

            // The first non-empty line must be the newest version's heading.
            var headingText = trimmed.TrimStart('#').Trim();

            // Drop any trailing note such as "(tombstoned)".
            var spaceIndex = headingText.IndexOf(' ');
            if (spaceIndex > 0)
            {
                headingText = headingText.Substring(0, spaceIndex);
            }

            if (int.TryParse(headingText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
                && version > 0)
            {
                return version;
            }

            return null;
        }

        return null;
    }
}
