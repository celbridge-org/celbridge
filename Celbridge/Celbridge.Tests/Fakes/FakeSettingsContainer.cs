using Celbridge.BaseLibrary.Core;
using Celbridge.BaseLibrary.Settings;
using CommunityToolkit.Diagnostics;
using Newtonsoft.Json;

namespace Celbridge.Tests.Fakes;

/// <summary>
/// A fake implementation of ISettingsContainer for use with automated tests.
/// We can't use the real SettingsContainer because it has a dependency on Windows.Storage which isn't available to tests.
/// </summary>
public class FakeSettingsContainer : ISettingsContainer
{
    private string? _containerName;
    private Dictionary<string, object>? _container;

    public void Initialize(string containerName)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new ArgumentNullException(nameof(containerName));
        }

        if (!string.IsNullOrEmpty(_containerName))
        {
            throw new InvalidOperationException("Settings container has already been initialized.");
        }

        _containerName = containerName;
        _container = new Dictionary<string, object>();
    }

    public void SetValue<T>(string key, T value) where T : notnull
    {
        Guard.IsNotNull(_container);

        string json = JsonConvert.SerializeObject(value);
        _container[key] = json;
    }

    public T GetValue<T>(string key) where T : notnull
    {
        Guard.IsNotNull(_container);

        if (!_container.TryGetValue(key, out object? json))
        {
            return default(T) ?? throw new InvalidOperationException();
        }

        var value = JsonConvert.DeserializeObject<T>((string)json);
        if (value is null)
        {
            throw new InvalidOperationException();
        }

        return value;
    }

    public T GetValue<T>(string key, T defaultValue) where T : notnull
    {
        Guard.IsNotNull(_container);

        if (!_container.TryGetValue(key, out object? json))
        {
            return defaultValue;
        }

        var value = JsonConvert.DeserializeObject<T>((string)json);
        if (value is null)
        {
            throw new InvalidOperationException();
        }

        return value;
    }


    public bool ContainsKey(string key)
    {
        Guard.IsNotNull(_container);

        return _container.ContainsKey(key);
    }

    public Result DeleteValue(string key)
    {
        Guard.IsNotNull(_container);

        if (!_container.Remove(key))
        {
            return Result.Fail($"Failed to remove setting '{_containerName}.{key}' because it was not found.");
        }

        return Result.Ok();
    }

    public void Reset()
    {
        Guard.IsNotNull(_container);

        _container.Clear();
    }
}
