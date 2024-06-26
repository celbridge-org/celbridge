using Celbridge.BaseLibrary.Commands;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Creates a new project using the specified config information.
/// </summary>
public interface ICreateProjectCommand : IExecutableCommand
{
    NewProjectConfig? Config { get; set; }
}
