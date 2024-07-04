using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Delete a file resource from the project.
/// </summary>
public interface IDeleteFileCommand : IExecutableCommand
{
    /// <summary>
    /// Project-relative path to the file to delete.
    /// </summary>
    string FilePath { get; set; }
}
