using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Copies a resource to the clipboard.
/// </summary>
public interface ICopyResourceCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key to copy to the clipboard.
    /// </summary>
    ResourceKey ResourceKey { get; set; }
}
