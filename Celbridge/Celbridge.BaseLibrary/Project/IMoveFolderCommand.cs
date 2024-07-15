using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Move a folder resource to a different path in the project.
/// </summary>
public interface IMoveFolderCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key of the folder to be moved.
    /// </summary>
    string FromResourceKey { get; set; }

    /// <summary>
    /// Resource key to move the folder to.
    /// </summary>
    string ToResourceKey { get; set; }
}
