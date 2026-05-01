using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Writes text content to a file. Creates the file if it does not exist.
/// For existing files, replaces the entire content. Writes directly to disk.
/// Any open document reloads its buffer from disk after the write.
/// </summary>
public interface IWriteFileCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the file to write.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// The new text content for the file.
    /// </summary>
    string Content { get; set; }
}
