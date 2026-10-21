using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Services.Roots;

namespace Celbridge.Resources.Services;

public class ResourceRegistry : IResourceRegistry
{
    private readonly ILogger<ResourceRegistry> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IProjectTreeBuilder _projectTreeBuilder;
    private readonly IResourceClassifier _resourceClassifier;
    private readonly RootHandlerRegistry _rootHandlerRegistry;

    private string _projectFolderPath = string.Empty;

    public string ProjectFolderPath => _projectFolderPath;

    public void InitializeProjectRoot(string projectFolderPath)
    {
        Guard.IsNotNullOrEmpty(projectFolderPath);

        _projectFolderPath = projectFolderPath;
        _rootHandlerRegistry.RegisterRootHandler(
            new ProjectRootHandler(projectFolderPath));
    }

    private FolderResource _projectFolder = new FolderResource(string.Empty, null);

    public IFolderResource ProjectFolder => _projectFolder;

    public ResourceRegistry(
        ILogger<ResourceRegistry> logger,
        IMessengerService messengerService,
        IProjectTreeBuilder projectTreeBuilder,
        IResourceClassifier resourceClassifier,
        RootHandlerRegistry rootHandlerRegistry)
    {
        _logger = logger;
        _messengerService = messengerService;
        _projectTreeBuilder = projectTreeBuilder;
        _resourceClassifier = resourceClassifier;
        _rootHandlerRegistry = rootHandlerRegistry;
    }

    public ResourceKey GetResourceKey(IResource resource)
    {
        return ResourceTreeNavigator.BuildKey(resource);
    }

    public List<ResourceKey> GetResourceKeys(IEnumerable<IResource> resources)
    {
        return resources.Select(GetResourceKey).ToList();
    }

    public Result<ResourceKey> GetResourceKey(string resourcePath)
    {
        return _rootHandlerRegistry.GetResourceKey(resourcePath);
    }

    public Result<string> ResolveResourcePath(IResource resource)
    {
        var resourceKey = GetResourceKey(resource);
        return ResolveResourcePath(resourceKey);
    }

    public Result<string> ResolveResourcePath(ResourceKey resource, bool validateCase = true)
    {
        return _rootHandlerRegistry.ResolveResourcePath(resource);
    }

    public Result<IResource> GetResource(ResourceKey resource)
    {
        // The registry tracks only the project tree; other roots have no IResource nodes.
        if (resource.Root != ResourceKey.DefaultRoot)
        {
            return Result<IResource>.Fail(
                $"GetResource is scoped to the project tree; root '{resource.Root}' has no tracked resources.");
        }

        return ResourceTreeNavigator.FindResource(_projectFolder, resource);
    }

    public async Task<Result> UpdateResourceRegistryAsync()
    {
        try
        {
            // Build a fresh tree off to the side, then atomically swap _projectFolder.
            // Readers on other threads see either the old or the new tree, never a torn
            // intermediate state. Once a tree has been observed it is immutable, so
            // iterators on Children remain valid even if a swap happens during a read.
            // Volatile.Write adds a release fence so the tree's construction writes are
            // visible before the new reference (a no-op on x64, required on ARM64).
            var buildResult = await _projectTreeBuilder.BuildTreeAsync();
            if (buildResult.IsFailure)
            {
                return Result.Fail("Failed to build the project tree.")
                    .WithErrors(buildResult);
            }
            var newRoot = (FolderResource)buildResult.Value;

            // Sidecar pairing runs on the new tree before publication. The
            // classifier sets each parent FileResource.Sidecar in place. The
            // root handler registry is handed in so per-sidecar path resolution
            // goes through the same reparse-point gateway as every other
            // resource operation.
            _resourceClassifier.ClassifyResources(newRoot, _rootHandlerRegistry);

            Volatile.Write(ref _projectFolder, newRoot);

            try
            {
                _messengerService.Send(new ResourceRegistryUpdatedMessage());
            }
            catch (Exception)
            {
                // Message handlers (e.g., UI tree rebuild) may throw transient
                // exceptions such as COMException when the visual tree is in a
                // transitional state. These are safe to ignore because the
                // registry update itself has already completed successfully.
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when updating the resource registry.")
                .WithException(ex);
        }
    }
}
