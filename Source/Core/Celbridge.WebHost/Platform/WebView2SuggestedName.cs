using System.Net.Http.Headers;

using Path = System.IO.Path;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Recovers the name a server gave a download from the one WebView2 suggests. Chromium checks the
/// operating system's Downloads folder before suggesting a name, and appends " (N)" when a file of that
/// name is already there. That folder is not where the file is going, so its suffix would make a download
/// look renamed for no reason, and the download service uniquifies against the real destination anyway.
/// WebKit suggests the server's name without checking any folder, so a macOS download has nothing to undo.
/// </summary>
internal static class WebView2SuggestedName
{
    /// <summary>
    /// Returns the server's name for the download when Chromium's suggestion is that name with its own
    /// " (N)" added, and Chromium's suggestion otherwise. Chromium also sanitises names, so its suggestion
    /// is kept wherever it cannot be shown to be only a uniquified form of the server's.
    /// </summary>
    public static string Resolve(string suggestedFilePath, string contentDisposition, string sourceUrl)
    {
        var suggestedName = Path.GetFileName(suggestedFilePath);

        var candidates = new List<string>
        {
            ReadContentDispositionName(contentDisposition),
            ReadUrlName(sourceUrl)
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            if (string.Equals(suggestedName, candidate, StringComparison.Ordinal) ||
                IsUniquifiedFrom(suggestedName, candidate))
            {
                return candidate;
            }
        }

        return suggestedName;
    }

    private static string ReadContentDispositionName(string contentDisposition)
    {
        if (string.IsNullOrEmpty(contentDisposition) ||
            !ContentDispositionHeaderValue.TryParse(contentDisposition, out var header))
        {
            return string.Empty;
        }

        // The encoded form is the one a server uses for a name plain ASCII cannot carry, so it wins.
        var name = header.FileNameStar;
        if (string.IsNullOrEmpty(name))
        {
            name = header.FileName;
        }

        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        // Only the last segment, so a header naming a path cannot direct the file anywhere.
        return Path.GetFileName(name.Trim('"'));
    }

    private static string ReadUrlName(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var lastSegment = uri.Segments.LastOrDefault() ?? string.Empty;
        var name = Uri.UnescapeDataString(lastSegment.TrimEnd('/'));

        return Path.GetFileName(name);
    }

    // True when name is original with " (N)" inserted before its extension, which is the only change
    // Chromium's uniquifier makes.
    private static bool IsUniquifiedFrom(string name, string original)
    {
        var stem = Path.GetFileNameWithoutExtension(original);
        var extension = Path.GetExtension(original);

        var prefix = $"{stem} (";
        var suffix = $"){extension}";

        if (name.Length <= prefix.Length + suffix.Length ||
            !name.StartsWith(prefix, StringComparison.Ordinal) ||
            !name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var counter = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);

        return counter.All(char.IsAsciiDigit);
    }
}
