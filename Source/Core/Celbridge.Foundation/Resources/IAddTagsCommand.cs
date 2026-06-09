using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Atomically appends a batch of tags to the parent resource's .cel sidecar
/// tag list. Creates the sidecar if it does not exist. Idempotent: already
/// present tags do not duplicate or rewrite the file.
/// </summary>
public interface IAddTagsCommand : IExecutableCommand
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be updated.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Tag values to append. Tags that are already present are no-ops;
    /// duplicates within the input list collapse to one append.
    /// </summary>
    IReadOnlyList<string> Tags { get; set; }
}
