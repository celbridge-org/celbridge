using Celbridge.Commands;

namespace Celbridge.Explorer;

/// <summary>
/// Display the Extract Archive dialog to allow the user to extract a zip archive to a folder.
/// </summary>
public interface IUnarchiveResourceDialogCommand : IExecutableCommand
{
    /// <summary>
    /// The zip archive resource to extract.
    /// </summary>
    ResourceKey ArchiveResource { get; set; }
}
