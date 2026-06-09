using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Atomically removes a batch of fields from the parent resource's .cel sidecar.
/// Missing names are silent no-ops; underscore-prefixed names are ignored at
/// this surface.
/// </summary>
public interface IRemoveFieldsCommand : IExecutableCommand
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be updated.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Field names to remove. Names that are not present on the sidecar are
    /// silent no-ops. Names beginning with the reserved underscore prefix are
    /// ignored — use the dedicated tag tools for the tag list.
    /// </summary>
    IReadOnlyList<string> Names { get; set; }
}
