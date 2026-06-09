using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Enumerates the unique tag values across every healthy .cel sidecar in the
/// workspace. Broken sidecars are skipped; they surface through IInspectCommand
/// instead. Results are sorted for diff stability.
/// </summary>
public interface IListTagsCommand : IExecutableCommand<IReadOnlyList<string>>
{
}
