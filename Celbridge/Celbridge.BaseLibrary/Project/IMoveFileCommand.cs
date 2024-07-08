using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Move a file resource to a different path in the project.
/// </summary>
public interface IMoveFileCommand : IExecutableCommand
{
    /// <summary>
    /// The file resource to move.
    /// </summary>
    string FromFilePath { get; set; }

    /// <summary>
    /// Path to move the file resource to.
    /// </summary>
    string ToFilePath { get; set; }
}
