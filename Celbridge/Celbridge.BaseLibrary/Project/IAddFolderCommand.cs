using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Add a folder resource to the project.
/// </summary>
public interface IAddFolderCommand : IExecutableCommand
{
    /// <summary>
    /// Resource path for the new folder to create.
    /// </summary>
    string ResourcePath { get; set; }
}
