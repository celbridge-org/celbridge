using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Delete a folder resource from the project.
/// </summary>
public interface IDeleteFolderCommand : IExecutableCommand
{
    /// <summary>
    /// Project-relative path to the folder to delete.
    /// </summary>
    string FolderPath { get; set; }
}
