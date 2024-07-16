using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Delete a file or folder resource from the project.
/// </summary>
public interface IDeleteResourceCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to delete.
    /// </summary>
    ResourceKey ResourceKey { get; set; }
}
