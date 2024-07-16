using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Copies a resource to the clipboard.
/// </summary>
public interface ICopyResourceCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to copy to the clipboard.
    /// </summary>
    ResourceKey ResourceKey { get; set; }

    /// <summary>
    /// If set to true, the original resource will be moved to the new location 
    /// when the paste is performed. In the context of the clipboard, this is a "cut".
    /// </summary>
    bool Move { get; set; }
}
