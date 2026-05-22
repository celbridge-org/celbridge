using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// A single project: reference that does not resolve to an existing resource.
/// Source is the file that contains the reference literal; MissingTarget is the
/// resource key the literal points to.
/// </summary>
public record BrokenReference(ResourceKey Source, ResourceKey MissingTarget);

/// <summary>
/// A .cel file that the registry tracks as not paired with a parent file. The
/// user / agent resolves orphans by deleting them, renaming the parent to claim
/// the sidecar, or creating a new file at the parent path.
/// </summary>
public record OrphanSidecar(ResourceKey Sidecar);

/// <summary>
/// A .cel file whose frontmatter does not parse cleanly. Covers merge-conflict
/// markers, malformed TOML, missing fences, and any other parse failure — the
/// host does not differentiate between these post-Phase-1 of the redesign.
/// Files ending in .cel.cel are also classified Broken via this category.
/// </summary>
public record BrokenSidecar(ResourceKey Sidecar);

/// <summary>
/// Structured project health report produced by IProjectCheckCommand. Empty
/// lists mean the corresponding invariant holds. The command does not repair
/// any of the surfaced issues; it is a pure read.
/// </summary>
public record ProjectCheckReport(
    IReadOnlyList<BrokenReference> BrokenReferences,
    IReadOnlyList<OrphanSidecar> OrphanSidecars,
    IReadOnlyList<BrokenSidecar> BrokenSidecars);

/// <summary>
/// Read-only check that surfaces dangling project: references and any sidecar
/// in an attention state (orphan, broken). Invoked at workspace load and
/// exposed as the data_check_project MCP tool.
/// </summary>
public interface IProjectCheckCommand : IExecutableCommand<ProjectCheckReport>
{
}
