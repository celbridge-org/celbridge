using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Delete a file resource from the project.
/// </summary>
public interface IDeleteFileCommand : IExecutableCommand
{
    /// <summary>
    /// Resource path for the file to delete.
    /// </summary>
    string ResourcePath { get; set; }
}
