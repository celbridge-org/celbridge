using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Deletes complete lines from a file, removing them entirely including their line terminators.
/// Uses 1-based line numbers. Both StartLine and EndLine are inclusive.
/// </summary>
public interface IDeleteLinesCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the file to delete lines from.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// First line to delete (1-based, inclusive).
    /// </summary>
    int StartLine { get; set; }

    /// <summary>
    /// Last line to delete (1-based, inclusive).
    /// </summary>
    int EndLine { get; set; }
}
