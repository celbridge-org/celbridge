using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Move a file resource to a different path in the project.
/// </summary>
public interface IMoveFileCommand : IExecutableCommand
{
    /// <summary>
    /// Resource path of the file to be moved.
    /// </summary>
    string FromResourcePath { get; set; }

    /// <summary>
    /// Resource path to move the file to.
    /// </summary>
    string ToResourcePath { get; set; }
}
