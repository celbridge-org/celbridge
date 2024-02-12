using Celbridge.BaseLibrary.Core;
using Celbridge.BaseLibrary.Settings;

namespace Celbridge.Tests.Fakes;

/// <summary>
/// A fake implementation of IApplicationSettings for use with automated tests.
/// </summary>
public class FakeApplicationSettings : IApplicationSettings
{
    private Dictionary<string, object> _settings = new Dictionary<string, object>();

    public Result SetValue<T>(string settingKey, T value) where T : notnull
    {
        _settings[settingKey] = value;
        return Result.Ok();
    }

    public Result<T> GetValue<T>(string settingKey) where T : notnull
    {
        if (_settings.TryGetValue(settingKey, out object? value))
        {
            var v = (T)value;
            if (v != null)
            {
                return Result<T>.Ok(v);
            }
        }

        return Result<T>.Fail("Failed to get value");
    }
}
