using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Add a file resource to the project.
/// </summary>
public interface IAddFileCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key for the new file to create.
    /// </summary>
    ResourceKey ResourceKey { get; set; }
}
