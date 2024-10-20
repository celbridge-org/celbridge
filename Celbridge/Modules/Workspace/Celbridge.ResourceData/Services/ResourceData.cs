using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using Path = System.IO.Path;

namespace Celbridge.ResourceData.Services;

public class ResourceData
{
    private readonly JObject _jsonData;
    private readonly ConcurrentDictionary<object, Action<ResourceKey, string>> _notifiers;
    private bool _isModified;

    public ResourceKey Resource { get; private set; }
    public string ResourceDataPath { get; private set; } = string.Empty;

    public ResourceData()
    {
        _jsonData = new JObject();
        _notifiers = new ConcurrentDictionary<object, Action<ResourceKey, string>>();
        _isModified = false;

        EnsurePropertiesExists();
    }

    public void SetResourceKey(ResourceKey resource, string resourceDataPath)
    {
        Resource = resource;
        ResourceDataPath = resourceDataPath;
    }

    /// <summary>
    /// Loads the resource data from disk. If the file doesn't exist, initializes an empty JObject with Properties.
    /// </summary>
    public Result Load(ResourceKey resource, string resourceDataPath)
    {
        try
        {
            Resource = resource;
            ResourceDataPath = resourceDataPath;

            if (File.Exists(resourceDataPath))
            {
                string jsonContent = File.ReadAllText(resourceDataPath);
                var parsedData = JObject.Parse(jsonContent);
                _jsonData.Merge(parsedData, new JsonMergeSettings
                {
                    MergeArrayHandling = MergeArrayHandling.Replace
                });

                EnsurePropertiesExists();
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading resource data for '{Resource}'")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Saves the resource data to disk if it has been modified.
    /// </summary>
    public async Task<Result> SaveAsync()
    {
        if (!_isModified)
        {
            return Result.Ok();
        }

        try
        {
            string jsonContent = _jsonData.ToString(Formatting.Indented);

            var folder = Path.GetDirectoryName(ResourceDataPath);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using (var writer = new StreamWriter(ResourceDataPath))
            {
                await writer.WriteAsync(jsonContent);
            }

            _isModified = false; // Reset modification status after saving
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save resource data for '{Resource}'").WithException(ex);
        }
    }

    /// <summary>
    /// Gets the value of a property from the "Properties" object in the root.
    /// </summary>
    public Result<T> GetProperty<T>(string propertyName, T defaultValue = default(T)!) where T : notnull
    {
        try
        {
            EnsurePropertiesExists();

            var properties = (JObject)_jsonData["Properties"]!;
            if (properties.ContainsKey(propertyName))
            {
                var value = properties[propertyName].ToObject<T>();
                return value is null ? Result<T>.Ok(defaultValue) : Result<T>.Ok(value);
            }

            return Result<T>.Ok(defaultValue);
        }
        catch (Exception ex)
        {
            return Result<T>.Fail($"Failed to get property '{propertyName}' from resource '{Resource}'").WithException(ex);
        }
    }

    /// <summary>
    /// Sets the value of a property in the "Properties" object in the root.
    /// </summary>
    public Result SetProperty<T>(string propertyName, T newValue) where T : notnull
    {
        try
        {
            EnsurePropertiesExists();

            var properties = (JObject)_jsonData["Properties"]!;
            properties[propertyName] = JToken.FromObject(newValue);

            _isModified = true; // Compare the previous and new values
            NotifyChanges(propertyName);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to set property '{propertyName}' in resource '{Resource}'").WithException(ex);
        }
    }

    /// <summary>
    /// Registers a callback to be triggered when a property is modified.
    /// </summary>
    public void RegisterNotifier(object recipient, Action<ResourceKey, string> callback)
    {
        _notifiers[recipient] = callback;
    }

    /// <summary>
    /// Unregisters a callback for the given recipient.
    /// </summary>
    public void UnregisterNotifier(object recipient)
    {
        _notifiers.TryRemove(recipient, out _);
    }

    private void NotifyChanges(string propertyName)
    {
        foreach (var notifier in _notifiers)
        {
            notifier.Value.Invoke(Resource, propertyName);
        }
    }

    private void EnsurePropertiesExists()
    {
        // Ensure the Properties object exists in the root
        if (!_jsonData.ContainsKey("Properties"))
        {
            _jsonData["Properties"] = new JObject();
        }
    }
}

