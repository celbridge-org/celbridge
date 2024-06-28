using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Add a folder resource to the project.
/// </summary>
public interface IAddFolderCommand : IExecutableCommand
{
    string FolderPath { get; set; }
}
