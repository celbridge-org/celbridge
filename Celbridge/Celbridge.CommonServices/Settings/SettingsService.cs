using Celbridge.BaseLibrary.Core;
using Celbridge.BaseLibrary.Settings;

namespace Celbridge.CommonServices.Settings;

public class SettingsService : ISettingsService
{
    private IApplicationSettings _applicationSettings;

    public SettingsService(IApplicationSettings applicationSettings)
    {
        _applicationSettings = applicationSettings;
    }

    public Result SetValue<T>(string containerName, string key, T value) where T : notnull
    {
        return _applicationSettings.SetValue(containerName, key, value);
    }

    public Result<T> GetValue<T>(string containerName, string key) where T : notnull
    {
        return _applicationSettings.GetValue<T>(containerName, key);
    }

    public bool ContainsValue(string containerName, string key)
    {
        return _applicationSettings.ContainsValue(containerName, key);
    }

    public Result DeleteValue(string containerName, string key)
    {
        return _applicationSettings.DeleteValue(containerName, key);
    }

    public void DeleteAll(string containerName)
    {
        _applicationSettings.DeleteAll(containerName);
    }
}
