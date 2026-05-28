using Celbridge.UserInterface;

namespace Celbridge.Resources;

/// <summary>
/// Parse health of a .cel file's content. Healthy means the frontmatter parses
/// cleanly; Broken means it does not (malformed TOML, merge-conflict markers,
/// missing fences, or any other parse failure). Applies to any .cel file —
/// paired sidecar, standalone, or orphan.
/// </summary>
public enum CelFileStatus
{
    Healthy,
    Broken,
}

/// <summary>
/// Link from a parent file to its paired .cel sidecar, carrying the sidecar's
/// resource key and current parse state.
/// </summary>
public partial record SidecarLink(ResourceKey Key, CelFileStatus Status);

/// <summary>
/// The role a file resource plays in the project resource taxonomy. Populated
/// by the resource classifier during project load and refreshed on every
/// resource registry update. Orthogonal to parse health: a Sidecar, Standalone,
/// or Orphan can independently be Healthy or Broken.
/// </summary>
public enum FileKind
{
    /// <summary>A non-.cel file (e.g. notes.md, image.png).</summary>
    PlainData,

    /// <summary>
    /// A .cel file paired with a parent content file in the same folder
    /// (e.g. notes.md.cel paired with notes.md). Holds frontmatter and
    /// content-block metadata for the parent.
    /// </summary>
    Sidecar,

    /// <summary>
    /// A parentless .cel file recognized as a registered standalone form
    /// (e.g. page.webview.cel, sprite.note.cel). Holds both metadata and
    /// content for a custom document type.
    /// </summary>
    Standalone,

    /// <summary>
    /// A parentless .cel file with no registered standalone form claiming it.
    /// Usually a sidecar whose parent was renamed or deleted, or a custom
    /// document type that is no longer installed.
    /// </summary>
    Orphan,

    /// <summary>
    /// A .cel file that fails the structural rules for a sidecar (e.g. a
    /// .cel.cel file). Distinct from CelFileStatus.Broken, which describes a
    /// well-shaped sidecar whose content failed to parse.
    /// </summary>
    InvalidSidecar,
}

/// <summary>
/// A file resource in the project folder.
/// </summary>
public interface IFileResource : IResource
{
    /// <summary>
    /// The icon to display for the file resource.
    /// </summary>
    public FileIconDefinition Icon { get; }

    /// <summary>
    /// The role this file plays in the project resource taxonomy.
    /// </summary>
    public FileKind FileKind { get; }

    /// <summary>
    /// The paired sidecar for this file, or null if no sidecar exists.
    /// Null also applies to .cel files (which do not have sidecars of their own).
    /// </summary>
    public SidecarLink? Sidecar { get; }
}
