using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// A single project: reference that does not resolve to an existing resource.
/// Source is the file that contains the reference literal; MissingTarget is the
/// resource key the literal points to.
/// </summary>
public record BrokenReference(ResourceKey Source, ResourceKey MissingTarget);

/// <summary>
/// Structured project health report. Empty lists mean the corresponding
/// invariant holds.
/// </summary>
public record ProjectCheckReport(
    IReadOnlyList<BrokenReference> BrokenReferences,
    IReadOnlyList<ResourceKey> OrphanCelFiles,
    IReadOnlyList<ResourceKey> BrokenCelFiles);

/// <summary>
/// Read-only check that surfaces dangling project: references and any .cel
/// file in an attention state (orphan, broken). Invoked at workspace load and
/// exposed as the data_check_project MCP tool.
/// </summary>
public interface IProjectCheckCommand : IExecutableCommand<ProjectCheckReport>
{
}
