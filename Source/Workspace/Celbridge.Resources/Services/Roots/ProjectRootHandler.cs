using Celbridge.Resources.Helpers;

namespace Celbridge.Resources.Services.Roots;

/// <summary>
/// Resource root handler for the visible project tree. Backs the implicit "project"
/// root used for every key without an explicit root prefix.
/// </summary>
public class ProjectRootHandler : ResourceRootHandlerBase
{
    private static readonly ResourceRootCapabilities ProjectCapabilities = new(
        IsWritable: true,
        IsWatched: true);

    public override string RootName => ResourceKey.DefaultRoot;
    public override ResourceRootCapabilities Capabilities => ProjectCapabilities;

    public ProjectRootHandler(string projectFolderPath, PathValidator pathValidator)
        : base(projectFolderPath, pathValidator)
    {
    }
}
