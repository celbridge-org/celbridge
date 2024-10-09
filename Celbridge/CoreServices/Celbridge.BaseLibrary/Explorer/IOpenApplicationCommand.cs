using Celbridge.Commands;
using Celbridge.Foundation;

namespace Celbridge.Explorer;

/// <summary>
/// Open a resource in the associated application.
/// </summary>
public interface IOpenApplicationCommand : IExecutableCommand
{
    /// <summary>
    /// The resource to open in the associated application.
    /// </summary>
    ResourceKey Resource { get; set; }
}
