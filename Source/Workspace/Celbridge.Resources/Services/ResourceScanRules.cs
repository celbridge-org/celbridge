namespace Celbridge.Resources.Services;

/// <summary>
/// Centralised policy for which files participate in the on-demand resource
/// scanner. Both the cascade rewrite path in <see cref="ResourceFileSystem"/>
/// and the broken-reference detection in <see cref="ResourceScanner"/> consume
/// this module so they cannot drift on what counts as a scannable file.
///
/// The exclusion list captured here is system-baseline, not user-configurable:
/// these extensions are deliberately invisible to the cascade because their
/// content is documentation rather than data. A future per-project configurable
/// include/exclude filter (see follow_up.md §10) layers on top — projects that
/// want to exclude additional extensions can do so; projects cannot opt back
/// in to scanning the baseline-excluded extensions.
/// </summary>
public static class ResourceScanRules
{
    /// <summary>
    /// File extensions excluded from reference scanning. Match is case-insensitive
    /// against the result of <see cref="System.IO.Path.GetExtension(string)"/>,
    /// which returns only the FINAL extension. A sidecar file paired to an
    /// excluded parent (e.g. <c>notes.md.cel</c>) carries the <c>.cel</c>
    /// extension under that rule, so the sidecar continues to be scanned even
    /// when its parent file would not be.
    ///
    /// Documentation file types only. Plain <c>.txt</c> stays scannable because
    /// it is the natural extension for fixtures and config-like data files
    /// whose embedded references should track.
    ///
    /// Adding a new exclusion: append the extension (with the leading dot,
    /// e.g. <c>".rst"</c>) to this set. The change reaches both the rename
    /// cascade and <c>data_check_project</c> automatically, and the
    /// <c>resource_keys</c> guide's "Excluded extensions" section should be
    /// updated to match.
    /// </summary>
    public static readonly IReadOnlySet<string> ExcludedExtensions
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Markdown is documentation. A quoted "project:..." literal in a
            // .md file is a descriptive mention, not an active link the
            // cascade should rewrite. Cascade tests, runbooks, READMEs, and
            // tool-feedback notes can quote tracked-reference forms freely.
            ".md",
        };

    /// <summary>
    /// True when files with the given extension are excluded from reference
    /// scanning. The extension argument must include the leading dot
    /// (e.g. <c>".md"</c>); pass the result of
    /// <see cref="System.IO.Path.GetExtension(string)"/> directly. An empty
    /// or null extension returns false (extensionless files are sniffed by
    /// the scanner via the text/binary heuristic).
    /// </summary>
    public static bool IsExcludedExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }
        return ExcludedExtensions.Contains(extension);
    }
}
