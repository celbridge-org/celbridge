using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Reads a single frontmatter field from the parent resource's .cel sidecar.
/// Fails when the resource has no sidecar, when the sidecar is broken, or
/// when the named field is not present.
/// </summary>
public interface IGetFieldCommand : IExecutableCommand<object>
{
    /// <summary>
    /// Parent resource whose .cel sidecar will be read.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Frontmatter field name to look up. The command fails when the field is
    /// not present on the sidecar.
    /// </summary>
    string Field { get; set; }
}
