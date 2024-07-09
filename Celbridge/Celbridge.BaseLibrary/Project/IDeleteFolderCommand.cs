using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Delete a folder resource from the project.
/// </summary>
public interface IDeleteFolderCommand : IExecutableCommand
{
    /// <summary>
    /// Resource path for the folder to delete.
    /// </summary>
    string ResourcePath { get; set; }
}
