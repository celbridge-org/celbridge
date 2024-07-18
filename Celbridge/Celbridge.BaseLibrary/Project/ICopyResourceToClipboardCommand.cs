using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Copies a resource to the clipboard.
/// The term "Clip" is used instead of "Copy" to distinguish this operation from 
/// copying a resource from one location to another.
/// </summary>
public interface ICopyResourceToClipboardCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to copy to the clipboard.
    /// </summary>
    ResourceKey ResourceKey { get; set; }

    /// <summary>
    /// If set to true, the original resource will be moved to the new location 
    /// when a paste is performed. This corresponds to a "cut" clipboard operation.
    /// </summary>
    bool MoveResource { get; set; }
}
