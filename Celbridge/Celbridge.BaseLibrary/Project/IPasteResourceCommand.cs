using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Pastes resource from the clipboard.
/// </summary>
public interface IPasteResourceCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key to paste the clipboard contents into.
    /// </summary>
    ResourceKey FolderResourceKey { get; set; }
}
