using Celbridge.Packages;
using Celbridge.Projects;

namespace Celbridge.Utilities;

/// <summary>
/// Recognizes Celbridge's own file formats: the project file, package manifests and per-contribution
/// editor manifests. The application depends on these files. Each format resolves to its own editor, and
/// Celbridge never writes a sidecar beside one of these files.
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
            || fileName.EndsWith(PackageConstants.EditorManifestExtension, StringComparison.Ordinal);
    }
}
