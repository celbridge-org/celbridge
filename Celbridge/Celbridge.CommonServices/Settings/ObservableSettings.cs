using Celbridge.BaseLibrary.Settings;
using System.ComponentModel;

namespace Celbridge.CommonServices.Settings;

/// <summary>
/// A wrapper for a named settings group which adds support for observing property changes.
/// </summary>
public abstract class ObservableSettings : INotifyPropertyChanged
{
    private ISettingsGroup _settingsGroup;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableSettings(ISettingsGroup settingsGroup, string groupName)
    {
        _settingsGroup = settingsGroup;
        _settingsGroup.Initialize(groupName);
    }

    public void Reset()
    {
        _settingsGroup.Reset();
    }

    protected T GetValue<T>(string settingName, T defaultValue)
        where T : notnull
    {
        return _settingsGroup.GetValue<T>(settingName, defaultValue );
    }

    protected void SetValue<T>(string settingName, T value)
        where T : notnull
    {
        if (_settingsGroup.ContainsValue(settingName, value))
        {
            // Previously stored value matches the value we are trying to set, so no change.
            return;
        }

        _settingsGroup.SetValue(settingName, value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(settingName));
    }
}
