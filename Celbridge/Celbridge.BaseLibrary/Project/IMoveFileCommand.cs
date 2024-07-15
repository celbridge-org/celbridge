using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Move a file resource to a different path in the project.
/// </summary>
public interface IMoveFileCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key of the file to be moved.
    /// </summary>
    ResourceKey FromResourceKey { get; set; }

    /// <summary>
    /// Resource key to move the file to.
    /// </summary>
    ResourceKey ToResourceKey { get; set; }
}
