using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Load the project file at the specified path.
/// </summary>
public interface ILoadProjectCommand : IExecutableCommand
{
    string ProjectFilePath { get; set; }
}
