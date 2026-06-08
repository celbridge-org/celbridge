using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Sets a single field in the parent resource's .cel sidecar. Creates the
/// sidecar if it does not exist.
/// </summary>
public interface ISetFieldCommand : IExecutableCommand
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be updated. Passing a sidecar
    /// key directly (e.g. notes.md.cel) is rejected at the tool layer.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Field name to write. Must be a non-empty TOML key.
    /// </summary>
    string Field { get; set; }

    /// <summary>
    /// Value to store. Must be an indexable scalar (string, number, bool)
    /// or a list of scalars; nested objects are rejected. Null fails the
    /// command rather than removing the field — use IRemoveFieldCommand for
    /// removal.
    /// </summary>
    object? Value { get; set; }
}
