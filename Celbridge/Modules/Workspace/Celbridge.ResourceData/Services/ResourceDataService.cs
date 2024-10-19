using CommunityToolkit.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

namespace Celbridge.ResourceData.Services;

public class ResourceDataService : IResourceDataService
{
    private readonly ConcurrentDictionary<ResourceKey, JObject> _loadedResources = new();
    private readonly ConcurrentDictionary<ResourceKey, ConcurrentDictionary<object, Action<ResourceKey, string>>> _notifiers = new(); 
    private readonly ConcurrentBag<ResourceKey> _modifiedResources = new(); // Track modified resources

    public Result AcquireResourceData(ResourceKey resource)
    {
        if (_loadedResources.ContainsKey(resource))
        {
            return Result.Ok();
        }

        try
        {
            string fullPath = GetFullPath(resource);
            if (File.Exists(fullPath))
            {
                string jsonContent = File.ReadAllText(fullPath);
                JObject jsonObject = JObject.Parse(jsonContent);
                _loadedResources[resource] = jsonObject;
            }
            else
            {
                _loadedResources[resource] = new JObject(); // Create a new empty JObject if the file doesn't exist
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading resource data: '{resource}'")
                .WithException(ex);
        }
    }

    public Result<T> GetValue<T>(ResourceKey resource, string jsonPath)
        where T : notnull
    {
        var obj = default(T);
        Guard.IsNotNull(obj);

        return GetValue(resource, jsonPath, obj);
    }

    public Result<T> GetValue<T>(ResourceKey resource, string jsonPath, T defaultValue)
        where T : notnull
    {
        var loadResult = AcquireResourceData(resource);
        if (loadResult.IsFailure)
        {
            return Result<T>.Fail("Failed to load resource data")
                .WithErrors(loadResult);
        }

        try
        {
            JObject loadedResource = _loadedResources[resource];

            JToken? token = loadedResource.SelectToken(jsonPath);
            if (token == null)
            {
                return Result<T>.Ok(defaultValue);
            }

            var obj = token.ToObject<T>();
            return obj is null ? Result<T>.Ok(defaultValue) : Result<T>.Ok(obj);
        }
        catch (Exception ex)
        {
            return Result<T>.Fail("An exception occurred when loading resource data")
                .WithException(ex);
        }
    }

    public Result SetValue<T>(ResourceKey resource, string jsonPath, T newValue)
        where T : notnull
    {
        var loadResult = AcquireResourceData(resource);
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load resource data: {resource}")
                .WithErrors(loadResult);
        }

        try
        {
            JObject loadedResource = _loadedResources[resource];

            JToken? token = loadedResource.SelectToken(jsonPath);
            if (token != null)
            {
                token.Replace(JToken.FromObject(newValue));
            }
            else
            {
                // Add new property if path does not exist
                loadedResource.SelectToken(jsonPath)?.Parent?.Add(newValue);
            }

            // Mark the resource as modified
            _modifiedResources.Add(resource);

            NotifyChanges(resource, jsonPath);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to set value: '{resource}', '{jsonPath}'")
                .WithException(ex);
        }

        return Result.Ok();
    }

    public void RegisterNotifier(ResourceKey resourceKey, object recipient, Action<ResourceKey, string> callback)
    {
        var recipientCallbacks = _notifiers.GetOrAdd(resourceKey, new ConcurrentDictionary<object, Action<ResourceKey, string>>());
        recipientCallbacks[recipient] = callback;
    }

    public void UnregisterNotifier(ResourceKey resourceKey, object recipient)
    {
        if (_notifiers.TryGetValue(resourceKey, out var recipientCallbacks))
        {
            recipientCallbacks.TryRemove(recipient, out _);

            // Clean up if no more recipients are listening for this resource key
            if (recipientCallbacks.IsEmpty)
            {
                _notifiers.TryRemove(resourceKey, out _);
            }
        }
    }

    public Result RemapResourceKey(ResourceKey oldResource, ResourceKey newResource)
    {
        try
        {
            // Update the loaded resources
            if (_loadedResources.ContainsKey(oldResource))
            {
                _loadedResources[newResource] = _loadedResources[oldResource];
                _loadedResources.TryRemove(oldResource, out _);
            }

            // Update the modified resources list
            if (_modifiedResources.Contains(oldResource))
            {
                _modifiedResources.Add(newResource);
                _modifiedResources.TryTake(out oldResource);
            }

            // Update the notifiers
            if (_notifiers.ContainsKey(oldResource))
            {
                _notifiers[newResource] = _notifiers[oldResource];
                _notifiers.TryRemove(oldResource, out _);
            }

            // Rename the backing JSON file
            string oldFilePath = GetFullPath(oldResource);
            string newFilePath = GetFullPath(newResource);
            if (File.Exists(oldFilePath))
            {
                File.Move(oldFilePath, newFilePath);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to remap resource: '{oldResource}' to '{newResource}'")
                .WithException(ex);
        }
    }

    public async Task<Result> SavePendingAsync()
    {
        foreach (var resourceKey in _modifiedResources)
        {
            if (_loadedResources.ContainsKey(resourceKey))
            {
                try
                {
                    string fullPath = GetFullPath(resourceKey);
                    string jsonContent = _loadedResources[resourceKey].ToString(Formatting.Indented);

                    using (var writer = new StreamWriter(fullPath))
                    {
                        await writer.WriteAsync(jsonContent);
                    }
                }
                catch (Exception ex)
                {
                    return Result.Fail($"An exception occurred when saving resource '{resourceKey}'")
                        .WithException(ex);
                }
            }
        }

        // Clear the modified resources list
        _modifiedResources.Clear();

        return Result.Ok();
    }

    private void NotifyChanges(ResourceKey resource, string jsonPath)
    {
        if (_notifiers.ContainsKey(resource))
        {
            foreach (var recipientCallback in _notifiers[resource])
            {
                recipientCallback.Value.Invoke(resource, jsonPath);
            }
        }
    }

    private string GetFullPath(ResourceKey resource)
    {
        // Todo: Map this to the file path in the CelData folder
        return resource;
    }
}
