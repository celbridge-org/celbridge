using Celbridge.Resources.Helpers;

namespace Celbridge.Resources.Services.Roots;

/// <summary>
/// Resource root handler for the visible project tree. Backs the implicit "project"
/// root used for every key without an explicit root prefix.
/// </summary>
public class ProjectRootHandler : IResourceRootHandler
{
    private static readonly ResourceRootCapabilities ProjectCapabilities = new(
        IsWritable: true,
        IsWatched: true);

    private readonly PathValidator _pathValidator;
    private readonly string _projectFolderPath;

    public string RootName => ResourceKey.DefaultRoot;
    public string BackingLocation => _projectFolderPath;
    public ResourceRootCapabilities Capabilities => ProjectCapabilities;

    public ProjectRootHandler(string projectFolderPath, PathValidator pathValidator)
    {
        _projectFolderPath = projectFolderPath;
        _pathValidator = pathValidator;
    }

    public Result<string> Resolve(ResourceKey key)
    {
        return _pathValidator.ValidateAndResolve(RootName, BackingLocation, key);
    }
}
