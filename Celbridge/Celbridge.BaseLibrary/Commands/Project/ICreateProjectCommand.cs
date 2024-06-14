using Celbridge.BaseLibrary.Project;

namespace Celbridge.BaseLibrary.Commands.Project;

/// <summary>
/// Creates a new project using the specified config information.
/// </summary>
public interface ICreateProjectCommand : ICommand
{
    NewProjectConfig? Config { get; set; }
}
