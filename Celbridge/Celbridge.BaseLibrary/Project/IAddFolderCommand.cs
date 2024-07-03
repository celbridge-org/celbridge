using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Add a folder resource to the project.
/// </summary>
public interface IAddFolderCommand : IExecutableCommand
{
    /// <summary>
    /// Project-relative path to the new folder.
    /// </summary>
    string FolderPath { get; set; }
}
