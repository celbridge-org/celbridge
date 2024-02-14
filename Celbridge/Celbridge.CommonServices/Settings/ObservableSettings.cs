using Celbridge.BaseLibrary.Settings;
using System.ComponentModel;

namespace Celbridge.CommonServices.Settings;

/// <summary>
/// A wrapper for a named settings container, with support for observing property changes.
/// </summary>
public abstract class ObservableSettings : INotifyPropertyChanged
{
    private ISettingsGroup _settingsContainer;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableSettings(ISettingsGroup settingsContainer, string containerName)
    {
        _settingsContainer = settingsContainer;
        _settingsContainer.Initialize(containerName);
    }

    public void Reset()
    {
        _settingsContainer.Reset();
    }

    protected T GetValue<T>(string settingName, T defaultValue)
        where T : notnull
    {
        return _settingsContainer.GetValue<T>(settingName, defaultValue );
    }

    protected void SetValue<T>(string settingName, T value)
        where T : notnull
    {
        if (_settingsContainer.ContainsValue(settingName, value))
        {
            // Previously stored value matches the value we are trying to set, so no change.
            return;
        }

        _settingsContainer.SetValue(settingName, value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(settingName));
    }
}
