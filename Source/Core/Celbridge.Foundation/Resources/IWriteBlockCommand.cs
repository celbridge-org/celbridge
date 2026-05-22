using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Creates or overwrites a named content block in the parent resource's .cel
/// sidecar. Creates the sidecar if it does not exist.
/// </summary>
public interface IWriteBlockCommand : IExecutableCommand
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be updated.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Identifier for the named block. Must match the block-naming rules
    /// (lowercase dotted segments). Overwrites any existing block with the
    /// same id; otherwise the block is appended.
    /// </summary>
    string BlockId { get; set; }

    /// <summary>
    /// Verbatim block body. Stored opaquely and round-trips through Parse /
    /// Compose unchanged; the only restriction is that it must not contain a
    /// line matching the fence regex ^\+\+\+ "...".
    /// </summary>
    string Content { get; set; }
}
