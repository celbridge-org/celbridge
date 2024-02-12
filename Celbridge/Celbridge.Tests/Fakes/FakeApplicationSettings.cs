using Celbridge.BaseLibrary.Core;
using Celbridge.BaseLibrary.Settings;

using Container = System.Collections.Generic.Dictionary<string, object>;

namespace Celbridge.Tests.Fakes;

/// <summary>
/// A fake implementation of IApplicationSettings for use with automated tests.
/// </summary>
public class FakeApplicationSettings : IApplicationSettings
{
    private Dictionary<string, Container> _containers = new();

    private Container GetContainer(string containerName)
    {
        if (_containers.TryGetValue(containerName, out var container))
        {
            return container;
        }

        container = new Container();
        _containers.Add(containerName, container);

        return container;
    }

    public Result SetValue<T>(string containerName, string settingKey, T value) where T : notnull
    {
        var settings = GetContainer(containerName);
        settings[settingKey] = value;
        return Result.Ok();
    }

    public Result<T> GetValue<T>(string containerName, string settingKey) where T : notnull
    {
        var settings = GetContainer(containerName);
        if (settings.TryGetValue(settingKey, out object? value))
        {
            var v = (T)value;
            if (v != null)
            {
                return Result<T>.Ok(v);
            }
        }

        return Result<T>.Fail($"Failed to get value for setting '{settingKey}'");
    }

    public bool ContainsValue(string containerName, string key)
    {
        var settings = GetContainer(containerName);
        return settings.ContainsKey(key);
    }

    public Result DeleteValue(string containerName, string key)
    {
        var settings = GetContainer(containerName);
        if (settings.Remove(key))
        {
            return Result.Ok();
        }
        return Result.Fail("Key not found");
    }

    public void DeleteAll(string containerName)
    {
        var settings = GetContainer(containerName);
        settings.Clear();
    }
}
