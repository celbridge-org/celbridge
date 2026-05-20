using Celbridge.UserInterface;

namespace Celbridge.Resources;

/// <summary>
/// The state of a paired .cel sidecar's content. Healthy means the frontmatter
/// parses cleanly; Broken means it does not (malformed TOML, merge-conflict
/// markers, missing fences, or any other parse failure). Absence of a sidecar
/// is expressed by a null SidecarInfo on the parent resource.
/// </summary>
public enum SidecarStatus
{
    Healthy,
    Broken,
}

/// <summary>
/// Identifies a paired sidecar and its current parse state.
/// </summary>
public partial record SidecarInfo(ResourceKey Key, SidecarStatus Status);

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
    /// The paired sidecar for this file, or null if no sidecar exists.
    /// Null also applies to .cel files (which do not have sidecars of their own).
    /// </summary>
    public SidecarInfo? Sidecar { get; }
}
