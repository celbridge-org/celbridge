using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Appends a tag to the parent resource's .cel sidecar tags list. Creates the
/// sidecar if it does not exist. Idempotent.
/// </summary>
public interface IAddTagCommand : IExecutableCommand
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be updated.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Tag value to append to the sidecar's "tags" list. Adding a tag that is
    /// already present is a no-op.
    /// </summary>
    string Tag { get; set; }
}
