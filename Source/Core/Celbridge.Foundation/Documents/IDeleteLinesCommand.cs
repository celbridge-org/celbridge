using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Deletes complete lines from a document, removing them entirely including their line terminators.
/// Uses 1-based line numbers. Both StartLine and EndLine are inclusive.
/// </summary>
public interface IDeleteLinesCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the document to delete lines from.
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
