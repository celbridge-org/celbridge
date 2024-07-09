using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Add a file resource to the project.
/// </summary>
public interface IAddFileCommand : IExecutableCommand
{
    /// <summary>
    /// Resource path for the new file to create.
    /// </summary>
    string ResourcePath { get; set; }
}
