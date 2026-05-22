using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Removes a named content block from the parent resource's .cel sidecar.
/// No-op when the block or the sidecar is absent.
/// </summary>
public interface IRemoveBlockCommand : IExecutableCommand
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be updated.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Identifier of the block to remove. The command succeeds silently when
    /// no block with this id exists.
    /// </summary>
    string BlockId { get; set; }
}
