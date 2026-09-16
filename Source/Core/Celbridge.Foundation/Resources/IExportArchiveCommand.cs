using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Writes a zip archive of a file or folder resource to a file outside the project, at a path the user chose
/// in the save dialog. When exporting a folder, the archive contains the folder's contents at the root.
/// </summary>
public interface IExportArchiveCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key of the file or folder to archive.
    /// </summary>
    ResourceKey SourceResource { get; set; }

    /// <summary>
    /// Absolute path of the zip file to write. An existing file at this path is replaced.
    /// </summary>
    string ArchiveFilePath { get; set; }
}
