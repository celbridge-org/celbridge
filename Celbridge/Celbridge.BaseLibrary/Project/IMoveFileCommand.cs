using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Move a file resource to a different path in the project.
/// </summary>
public interface IMoveFileCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key of the file to be moved.
    /// </summary>
    string FromResourceKey { get; set; }

    /// <summary>
    /// Resource key to move the file to.
    /// </summary>
    string ToResourceKey { get; set; }
}
