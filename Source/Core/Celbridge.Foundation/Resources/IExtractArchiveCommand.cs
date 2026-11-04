using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Result data returned by the extract archive command.
/// </summary>
public record class ExtractArchiveResult
{
    /// <summary>
    /// The number of files extracted from the archive.
    /// </summary>
    public required int Entries { get; init; }

    /// <summary>
    /// The resource key of the destination folder.
    /// </summary>
    public required string Destination { get; init; }

    /// <summary>
    /// The entries left out because the resource policy refused them. Each entry is named by its path inside the
    /// archive.
    /// </summary>
    public required IReadOnlyList<string> SkippedEntries { get; init; }
}

/// <summary>
/// Extracts a zip archive to a destination folder.
/// </summary>
public interface IExtractArchiveCommand : IExecutableCommand<ExtractArchiveResult>
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
    /// Replaces existing files when true.
    /// </summary>
    bool Overwrite { get; set; }
}
