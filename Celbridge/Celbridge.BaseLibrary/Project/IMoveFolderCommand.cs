using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Move a folder resource to a different path in the project.
/// </summary>
public interface IMoveFolderCommand : IExecutableCommand
{
    /// <summary>
    /// Project-relative path to the folder to move.
    /// </summary>
    string FromFolderPath { get; set; }

    /// <summary>
    /// Project-relative path to move the folder resource to
    /// </summary>
    string ToFolderPath { get; set; }
}
