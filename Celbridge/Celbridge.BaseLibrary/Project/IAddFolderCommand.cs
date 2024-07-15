using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Add a folder resource to the project.
/// </summary>
public interface IAddFolderCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key for the new folder to create.
    /// </summary>
    string ResourceKey { get; set; }
}
