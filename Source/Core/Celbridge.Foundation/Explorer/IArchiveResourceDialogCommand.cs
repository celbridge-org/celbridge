using Celbridge.Commands;

namespace Celbridge.Explorer;

/// <summary>
/// Display the Create Archive dialog to allow the user to create a zip archive from a folder.
/// </summary>
public interface IArchiveResourceDialogCommand : IExecutableCommand
{
    /// <summary>
    /// Folder resource to archive.
    /// </summary>
    ResourceKey FolderResource { get; set; }
}
