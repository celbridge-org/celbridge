namespace Celbridge.BaseLibrary.Commands.Project;

/// <summary>
/// Load the project at the specified path.
/// </summary>
public interface ILoadProjectCommand : ICommand
{
    string ProjectPath { get; set; }
}
