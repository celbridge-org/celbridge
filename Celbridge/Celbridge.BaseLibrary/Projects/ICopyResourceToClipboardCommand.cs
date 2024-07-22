using Celbridge.Commands;
using Celbridge.Resources;

namespace Celbridge.Projects;

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
    ResourceKey SourceResource { get; set; }

    /// <summary>
    /// If set to Move, the original resource will be moved to the new location 
    /// when the paste is performed. This corresponds to a "cut" clipboard operation.
    /// </summary>
    CopyResourceOperation Operation { get; set; }
}
