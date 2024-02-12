using Celbridge.BaseLibrary.Settings;

namespace Celbridge.CommonServices.Settings;

public class EditorSettings : IEditorSettings
{
    private ISettingsContainer _settingsContainer;

    public EditorSettings(ISettingsContainer settingsContainer)
    {
        _settingsContainer = settingsContainer;
        _settingsContainer.Initialize(nameof(EditorSettings));
    }

    public ApplicationColorTheme Theme
    {
        get => _settingsContainer.GetValue<ApplicationColorTheme>(nameof(ApplicationColorTheme));
        set => _settingsContainer.SetValue(nameof(ApplicationColorTheme), value);
    }

    public void Reset()
    {
        _settingsContainer.Reset();
    }
}
