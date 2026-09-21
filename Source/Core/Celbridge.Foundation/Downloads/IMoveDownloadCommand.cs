using Celbridge.Commands;

namespace Celbridge.Downloads;

/// <summary>
/// Moves a finished download from where it was staged under temp: to its place in the project. The move
/// is not recorded for undo, since a download is not an edit the user made. A source outside temp: is
/// refused.
/// </summary>
public interface IMoveDownloadCommand : IExecutableCommand
{
    /// <summary>
    /// The staged download, under temp:.
    /// </summary>
    ResourceKey SourceResource { get; set; }

    /// <summary>
    /// Where the download goes.
    /// </summary>
    ResourceKey DestResource { get; set; }
}
