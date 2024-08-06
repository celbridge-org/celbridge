using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Creates a new project using the specified config information.
/// </summary>
public interface ICreateProjectCommand : IExecutableCommand
{
    NewProjectConfig? Config { get; set; }
}
