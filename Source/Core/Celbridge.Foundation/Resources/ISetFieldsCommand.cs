using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Atomically writes a batch of fields to the parent resource's .cel sidecar.
/// Creates the sidecar if it does not exist. The full batch is applied in a
/// single read-modify-write, so a failure during validation leaves the file
/// unchanged.
/// </summary>
public interface ISetFieldsCommand : IExecutableCommand
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be updated.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Field name to value mapping. Each value must be an indexable scalar
    /// (string, number, bool, datetime) or a list of scalars; nested objects
    /// are rejected. Field names beginning with the reserved underscore prefix
    /// are rejected here — use the dedicated tag tools for the tag list.
    /// </summary>
    IReadOnlyDictionary<string, object> Fields { get; set; }
}
