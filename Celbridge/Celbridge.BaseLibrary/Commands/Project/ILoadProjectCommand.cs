namespace Celbridge.BaseLibrary.Commands.Project;

/// <summary>
/// Load the project at the specified path.
/// </summary>
public interface ILoadProjectCommand : IExecutableCommand
{
    string ProjectPath { get; set; }
}
