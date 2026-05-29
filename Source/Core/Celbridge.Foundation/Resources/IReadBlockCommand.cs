using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Reads the content of a named block from the parent resource's .cel sidecar.
/// Fails when the resource has no sidecar, when the sidecar is broken, or
/// when the named block is not present.
/// </summary>
public interface IReadBlockCommand : IExecutableCommand<string>
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be read.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Identifier of the block to return. The command fails when no block
    /// with this id exists, or when the id violates the block-naming rules.
    /// </summary>
    string BlockId { get; set; }
}
