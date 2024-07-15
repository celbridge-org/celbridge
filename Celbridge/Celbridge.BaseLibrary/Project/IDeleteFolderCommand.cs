using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Delete a folder resource from the project.
/// </summary>
public interface IDeleteFolderCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key for the folder to delete.
    /// </summary>
    string ResourceKey { get; set; }
}
