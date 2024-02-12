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

    public Result SetValue<T>(string settingKey, T value) where T : notnull
    {
        return _applicationSettings.SetValue(settingKey, value);
    }

    public Result<T> GetValue<T>(string settingKey) where T : notnull
    {
        return _applicationSettings.GetValue<T>(settingKey);
    }
}
