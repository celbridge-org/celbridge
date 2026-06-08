using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Removes a single field from the parent resource's .cel sidecar. No-op when
/// the field or the sidecar is absent.
/// </summary>
public interface IRemoveFieldCommand : IExecutableCommand
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be updated.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Field name to remove. The command succeeds silently when the field is
    /// absent.
    /// </summary>
    string Field { get; set; }
}
