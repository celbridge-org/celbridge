using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Workshop;

namespace Celbridge.Utilities;

/// <summary>
/// The file formats Celbridge's own machinery reads: the project file, package manifests,
/// per-contribution editor manifests, and page manifests. They carry a role the application depends on
/// rather than being project content, so they resolve to their own editor and no sidecar is written
/// beside them.
/// </summary>
public static class CelbridgeFileFormats
{
    /// <summary>
    /// True when the file name is one of Celbridge's own formats.
    /// </summary>
    public static bool IsCelbridgeFormat(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        return fileName.EndsWith(ProjectConstants.ProjectFileExtension, StringComparison.Ordinal)
            || fileName.Equals(PackageConstants.ManifestFileName, StringComparison.Ordinal)
            || fileName.EndsWith(PackageConstants.EditorManifestExtension, StringComparison.Ordinal)
            || fileName.Equals(PageConstants.ManifestFileName, StringComparison.Ordinal);
    }
}
