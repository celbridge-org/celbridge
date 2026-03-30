using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Result data returned by the archive resource command.
/// </summary>
public record class ArchiveResult
{
    /// <summary>
    /// The number of file entries added to the archive.
    /// </summary>
    public required int Entries { get; init; }

    /// <summary>
    /// The size of the archive file in bytes.
    /// </summary>
    public required long Size { get; init; }

    /// <summary>
    /// The resource key of the created archive.
    /// </summary>
    public required string Archive { get; init; }
}

/// <summary>
/// Creates a zip archive from a file or folder resource.
/// When archiving a folder, the archive contains the folder's contents at the root.
/// </summary>
public interface IArchiveResourceCommand : IExecutableCommand<ArchiveResult>
{
    /// <summary>
    /// Resource key of the file or folder to archive.
    /// </summary>
    ResourceKey SourceResource { get; set; }

    /// <summary>
    /// Resource key for the output zip file.
    /// </summary>
    ResourceKey ArchiveResource { get; set; }

    /// <summary>
    /// Semicolon-separated glob patterns to include (e.g. "*.py;*.md").
    /// When empty, all files are included.
    /// </summary>
    string Include { get; set; }

    /// <summary>
    /// Semicolon-separated glob patterns to exclude (e.g. "__pycache__;.git").
    /// </summary>
    string Exclude { get; set; }

    /// <summary>
    /// Whether to overwrite an existing archive.
    /// </summary>
    bool Overwrite { get; set; }
}
