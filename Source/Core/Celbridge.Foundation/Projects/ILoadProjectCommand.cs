using Celbridge.Commands;

namespace Celbridge.Projects;

/// <summary>
/// Load the project file at the specified path.
/// </summary>
public interface ILoadProjectCommand : IExecutableCommand
{
    string ProjectFilePath { get; set; }
}
