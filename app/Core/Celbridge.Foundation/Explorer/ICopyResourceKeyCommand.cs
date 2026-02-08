using Celbridge.Commands;

namespace Celbridge.Explorer;

/// <summary>
/// Copies a resource key to the clipboard.
/// </summary>
public interface ICopyResourceKeyCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key to copy to the clipboard.
    /// </summary>
    ResourceKey ResourceKey { get; set; }
}

