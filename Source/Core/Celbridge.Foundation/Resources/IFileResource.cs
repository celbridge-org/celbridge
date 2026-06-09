using Celbridge.UserInterface;

namespace Celbridge.Resources;

/// <summary>
/// Parse-state axis for a .cel file's content. Binary: parses cleanly or
/// doesn't. Orthogonal to FileKind (which carries the file's role —
/// Sidecar / Orphan / InvalidSidecar); the same .cel can independently be
/// any combination of the two. The agent-facing data_inspect surface projects
/// the two axes into a single SidecarStatus enum; consumers at the registry
/// layer keep them separate.
/// </summary>
public enum CelParseStatus
{
    /// <summary>
    /// The file parses cleanly as TOML.
    /// </summary>
    Healthy,

    /// <summary>
    /// The file failed to parse: malformed TOML, merge-conflict markers, or
    /// any other parse failure.
    /// </summary>
    Broken,
}

/// <summary>
/// Link from a parent file to its paired .cel sidecar, carrying the sidecar's
/// resource key and current parse state.
/// </summary>
public partial record SidecarLink(ResourceKey Key, CelParseStatus Status);

/// <summary>
/// The role a file resource plays in the project resource taxonomy. Populated
/// by the resource classifier during project load and refreshed on every
/// resource registry update. Orthogonal to parse health: a Sidecar or Orphan
/// can independently be Healthy or Broken.
/// </summary>
public enum FileKind
{
    /// <summary>A non-.cel file (e.g. notes.md, image.png).</summary>
    PlainData,

    /// <summary>
    /// A .cel file paired with a parent content file in the same folder
    /// (e.g. notes.md.cel paired with notes.md). Holds field metadata for
    /// the parent.
    /// </summary>
    Sidecar,

    /// <summary>
    /// A parentless .cel file. Usually a sidecar whose parent was renamed
    /// or deleted.
    /// </summary>
    Orphan,

    /// <summary>
    /// A .cel file that fails the structural rules for a sidecar (e.g. a
    /// .cel.cel file). Distinct from CelParseStatus.Broken, which describes a
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
