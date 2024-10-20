using Celbridge.Projects;
using Celbridge.Workspace;
using System.Collections.Concurrent;

using Path = System.IO.Path;

namespace Celbridge.ResourceData.Services;

public class ResourceDataService : IResourceDataService
{
    private readonly IProjectService _projectService;
    private readonly ConcurrentDictionary<ResourceKey, ResourceData> _resourceDataCache = new(); // Cache for ResourceData objects
    private readonly ConcurrentBag<ResourceKey> _modifiedResources = new(); // Track modified resources

    public ResourceDataService(
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _projectService = projectService;
    }

    /// <summary>
    /// Gets the value of a property from the "Properties" object in the root of the resource data.
    /// </summary>
    public Result<T> GetProperty<T>(ResourceKey resource, string propertyName, T defaultValue = default(T)!) where T : notnull
    {
        var loadResult = AcquireResourceData(resource);
        if (loadResult.IsFailure)
        {
            return Result<T>.Fail($"Failed to acquire resource data for '{resource}'")
                .WithErrors(loadResult);
        }

        return _resourceDataCache[resource].GetProperty(propertyName, defaultValue);
    }

    /// <summary>
    /// Sets the value of a property in the "Properties" object in the root of the resource data.
    /// </summary>
    public Result SetProperty<T>(ResourceKey resource, string propertyName, T newValue) where T : notnull
    {
        var loadResult = AcquireResourceData(resource);
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to acquire resource data for '{resource}'")
                .WithErrors(loadResult);
        }

        var setResult = _resourceDataCache[resource].SetProperty(propertyName, newValue);
        if (setResult.IsSuccess)
        {
            _modifiedResources.Add(resource); // Mark resource as modified
        }

        return setResult;
    }

    /// <summary>
    /// Registers a callback that gets triggered when a property in the resource is modified.
    /// </summary>
    public void RegisterNotifier(ResourceKey resourceKey, object recipient, Action<ResourceKey, string> callback)
    {
        var loadResult = AcquireResourceData(resourceKey);
        if (loadResult.IsSuccess)
        {
            _resourceDataCache[resourceKey].RegisterNotifier(recipient, callback);
        }
    }

    /// <summary>
    /// Unregisters a callback for the given resource key and recipient.
    /// </summary>
    public void UnregisterNotifier(ResourceKey resourceKey, object recipient)
    {
        var loadResult = AcquireResourceData(resourceKey);
        if (loadResult.IsSuccess)
        {
            _resourceDataCache[resourceKey].UnregisterNotifier(recipient);
        }
    }

    /// <summary>
    /// Remaps the old resource key to a new resource key.
    /// </summary>
    public Result RemapResourceKey(ResourceKey oldResource, ResourceKey newResource)
    {
        try
        {
            if (_resourceDataCache.ContainsKey(oldResource))
            {
                var resourceData = _resourceDataCache[oldResource];

                var newResourceDataPath = GetResourceDataPath(newResource);
                resourceData.SetResourceKey(newResource, newResourceDataPath);

                _resourceDataCache[newResource] = resourceData;
                _resourceDataCache.TryRemove(oldResource, out _);

                // Update the modified resources list
                if (_modifiedResources.Contains(oldResource))
                {
                    _modifiedResources.Add(newResource);
                    _modifiedResources.TryTake(out oldResource);
                }

                // Rename the backing JSON file
                string oldResourcePath = GetResourceDataPath(oldResource);
                string newResourcePath = GetResourceDataPath(newResource);
                if (File.Exists(oldResourcePath))
                {
                    File.Move(oldResourcePath, newResourcePath);
                }
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to remap resource: '{oldResource}' to '{newResource}'")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Saves all modified resources to disk asynchronously.
    /// </summary>
    public async Task<Result> SavePendingAsync()
    {
        foreach (var resourceKey in _modifiedResources)
        {
            if (_resourceDataCache.ContainsKey(resourceKey))
            {
                var resourceData = _resourceDataCache[resourceKey];

                var saveResult = await resourceData.SaveAsync();
                if (saveResult.IsFailure)
                {
                    return Result.Fail($"Failed to save resource data: '{resourceKey}'")
                        .WithErrors(saveResult);
                }
            }
        }

        // Clear the modified resources list
        _modifiedResources.Clear();

        return Result.Ok();
    }

    /// <summary>
    /// Acquires a ResourceData object for the given resource key.
    /// If it doesn't exist in the cache, it will load it from disk or create a new one.
    /// </summary>
    private Result AcquireResourceData(ResourceKey resource)
    {
        if (_resourceDataCache.ContainsKey(resource))
        {
            return Result.Ok();
        }

        try
        {
            // Create and load the ResourceData object
            var resourceData = new ResourceData();
            string resourcePath = GetResourceDataPath(resource);

            var loadResult = resourceData.Load(resource, resourcePath);
            if (loadResult.IsFailure)
            {
                return loadResult;
            }

            _resourceDataCache[resource] = resourceData;
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading resource data: '{resource}'")
                .WithException(ex);
        }
    }

    private string GetResourceDataPath(ResourceKey resourceKey)
    {
        var projectDataFolderPath = _projectService.CurrentProject!.ProjectDataFolderPath;
        var path = Path.Combine(projectDataFolderPath, "ResourceData", $"{resourceKey}.json");

        return Path.GetFullPath(path);
    }
}
