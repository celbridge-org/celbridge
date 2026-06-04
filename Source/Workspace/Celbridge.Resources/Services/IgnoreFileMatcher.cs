using GitIgnore = Ignore.Ignore;

namespace Celbridge.Resources.Services;

/// <summary>
/// Computes the ignore set for the resource policy from a gitignore-format file.
/// A path is ignored if the ignore-file matches it directly or matches any of
/// its ancestor folders (gitignore directory parent-pruning).
/// </summary>
internal interface IIgnoreFileMatcher
{
    /// <summary>
    /// Returns true if the resource path is matched by the ignore-file. The path
    /// is the ResourceKey's Path portion (forward-slash separator, no leading or
    /// trailing slash). The isFolder hint lets directory-only patterns (those
    /// ending with '/') match a folder of that name.
    /// </summary>
    bool IsIgnored(string resourcePath, bool isFolder);
}

/// <summary>
/// Wraps the goelhardik/Ignore package so the third-party type never leaks into
/// the policy engine. The wrapper adds ancestor-folder checking so a path under
/// an ignored folder is itself ignored regardless of how the underlying library
/// models directory patterns.
/// </summary>
internal sealed class IgnoreFileMatcher : IIgnoreFileMatcher
{
    private readonly GitIgnore _ignore;
    private readonly bool _isEmpty;

    public IgnoreFileMatcher(IEnumerable<string> patternLines)
    {
        _ignore = new GitIgnore();

        bool addedAny = false;
        foreach (var line in patternLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            // The library skips comment and blank lines, and supports '!' negation
            // within the file itself.
            _ignore.Add(line);
            addedAny = true;
        }

        _isEmpty = !addedAny;
    }

    public bool IsIgnored(string resourcePath, bool isFolder)
    {
        if (_isEmpty
            || string.IsNullOrEmpty(resourcePath))
        {
            return false;
        }

        var normalized = resourcePath.Replace('\\', '/').Trim('/');

        if (Matches(normalized, isFolder))
        {
            return true;
        }

        // Directory parent-pruning: a path nested under an ignored folder is
        // itself ignored. Walk each ancestor prefix and test it as a folder.
        int searchStart = 0;
        while (true)
        {
            int slashIndex = normalized.IndexOf('/', searchStart);
            if (slashIndex < 0)
            {
                break;
            }

            var ancestor = normalized.Substring(0, slashIndex);
            if (Matches(ancestor, isFolder: true))
            {
                return true;
            }

            searchStart = slashIndex + 1;
        }

        return false;
    }

    // The library matches purely on the string. A directory-only pattern such as
    // "build/" requires a trailing slash to match the folder itself, so folders
    // are tested both with and without the slash.
    private bool Matches(string path, bool isFolder)
    {
        if (_ignore.IsIgnored(path))
        {
            return true;
        }

        if (isFolder
            && _ignore.IsIgnored(path + "/"))
        {
            return true;
        }

        return false;
    }
}
