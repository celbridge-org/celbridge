using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Removes a tag from the parent resource's .cel sidecar tags list. Idempotent.
/// </summary>
public interface IRemoveTagCommand : IExecutableCommand
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be updated.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Tag value to drop from the sidecar's "tags" list. Removing a tag that
    /// is not present is a no-op; removing the final tag drops the "tags"
    /// field entirely.
    /// </summary>
    string Tag { get; set; }
}
