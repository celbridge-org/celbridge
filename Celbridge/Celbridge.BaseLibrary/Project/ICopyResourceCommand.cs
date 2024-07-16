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

    /// <summary>
    /// If true, the original resource will be moved to the new location when the paste is performed.
    /// </summary>
    bool Move { get; set; }
}
