using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Atomically removes a batch of tags from the parent resource's .cel sidecar
/// tag list. Idempotent: missing tags are no-ops. Removing the final tag drops
/// the tag list entirely.
/// </summary>
public interface IRemoveTagsCommand : IExecutableCommand
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be updated.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Tag values to drop from the sidecar's tag list. Tags that are not
    /// present are no-ops.
    /// </summary>
    IReadOnlyList<string> Tags { get; set; }
}
