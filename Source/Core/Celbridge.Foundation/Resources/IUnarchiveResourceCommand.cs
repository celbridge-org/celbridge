using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Result data returned by the unarchive resource command.
/// </summary>
public record class UnarchiveResult
{
    /// <summary>
    /// The number of files extracted from the archive.
    /// </summary>
    public required int Entries { get; init; }

    /// <summary>
    /// The resource key of the destination folder.
    /// </summary>
    public required string Destination { get; init; }
}

/// <summary>
/// Extracts a zip archive to a destination folder.
/// </summary>
public interface IUnarchiveResourceCommand : IExecutableCommand<UnarchiveResult>
{
    /// <summary>
    /// Resource key of the zip file to extract.
    /// </summary>
    ResourceKey ArchiveResource { get; set; }

    /// <summary>
    /// Resource key of the target folder.
    /// </summary>
    ResourceKey DestinationResource { get; set; }

    /// <summary>
    /// Whether to overwrite existing files.
    /// </summary>
    bool Overwrite { get; set; }
}
