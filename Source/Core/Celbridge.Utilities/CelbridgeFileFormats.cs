using Celbridge.Packages;
using Celbridge.Projects;

namespace Celbridge.Utilities;

/// <summary>
/// The file formats Celbridge's own machinery reads: the project file, package manifests,
/// per-contribution editor manifests, and page manifests. They carry a role the application depends on rather than being
/// project content, so the resource policy holds them addressable whatever the project's rules say, and
/// no sidecar is written beside them.
/// </summary>
public static class CelbridgeFileFormats
{
    /// <summary>
    /// Resource path patterns matching the formats, for a consumer that compiles them into rules.
    /// </summary>
    public static readonly IReadOnlyList<string> Patterns =
    [
        $"*{ProjectConstants.ProjectFileExtension}",
        PackageConstants.ManifestFileName,
        $"*{PackageConstants.EditorManifestExtension}",
        PageConstants.ManifestFileName,
    ];

    // Compiled once from Patterns, so the predicate and the rules a consumer builds cannot disagree.
    private static readonly IReadOnlyList<ResourcePathMatcher> Matchers =
        Patterns.Select(ResourcePathMatcher.Compile).ToList();

    /// <summary>
    /// True when the file name is one of Celbridge's own formats.
    /// </summary>
    public static bool IsCelbridgeFormat(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        foreach (var matcher in Matchers)
        {
            if (matcher.IsMatch(fileName, isFolder: false))
            {
                return true;
            }
        }

        return false;
    }
}
