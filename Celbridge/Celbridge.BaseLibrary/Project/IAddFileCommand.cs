using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Add a file resource to the project.
/// </summary>
public interface IAddFileCommand : IExecutableCommand
{
    /// <summary>
    /// Project-relative path to the new file.
    /// </summary>
    string FilePath { get; set; }
}
