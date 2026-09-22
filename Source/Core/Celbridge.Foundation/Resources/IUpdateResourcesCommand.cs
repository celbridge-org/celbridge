using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Update the resource registry and resource tree view to reflect the state of the files and folders on disk.
/// </summary>
public interface IUpdateResourcesCommand : IExecutableCommand
{
    /// <summary>
    /// When true, the project is rescanned before the command completes. When false (the default), the
    /// rescan is scheduled after a short quiet period, so requests from several sources coalesce into one.
    /// </summary>
    bool Immediate { get; set; }
}
