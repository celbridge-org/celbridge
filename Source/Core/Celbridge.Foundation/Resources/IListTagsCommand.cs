using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Enumerates the unique tag values across every healthy .cel sidecar in the
/// workspace. Broken sidecars are skipped; they surface through IInspectCommand
/// instead. Results are sorted for diff stability.
/// </summary>
public interface IListTagsCommand : IExecutableCommand<IReadOnlyList<string>>
{
    /// <summary>
    /// When true, the command leaves out the tags of what the project's search list excludes. True by default.
    /// </summary>
    bool ApplySearchExclude { get; set; }
}
