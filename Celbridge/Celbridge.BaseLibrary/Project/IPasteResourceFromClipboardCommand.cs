using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Pastes resources from the clipboard.
/// </summary>
public interface IPasteResourceFromClipboardCommand : IExecutableCommand
{
    /// <summary>
    /// Folder resource to paste the clipboard contents into.
    /// </summary>
    ResourceKey FolderResourceKey { get; set; }
}
