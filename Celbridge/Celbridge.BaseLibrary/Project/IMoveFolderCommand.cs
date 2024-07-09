using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Move a folder resource to a different path in the project.
/// </summary>
public interface IMoveFolderCommand : IExecutableCommand
{
    /// <summary>
    /// Resource path of the folder to be moved.
    /// </summary>
    string FromResourcePath { get; set; }

    /// <summary>
    /// Resource path to move the folder to.
    /// </summary>
    string ToResourcePath { get; set; }
}
