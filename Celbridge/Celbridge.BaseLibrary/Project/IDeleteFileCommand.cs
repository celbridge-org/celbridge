using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Delete a file resource from the project.
/// </summary>
public interface IDeleteFileCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key for the file to delete.
    /// </summary>
    ResourceKey ResourceKey { get; set; }
}
