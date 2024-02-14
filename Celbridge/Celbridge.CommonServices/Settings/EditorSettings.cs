using Celbridge.BaseLibrary.Settings;

namespace Celbridge.CommonServices.Settings;

public class EditorSettings : ObservableSettings, IEditorSettings
{
    public EditorSettings(ISettingsContainer settingsContainer)
        : base(settingsContainer, nameof(EditorSettings))
    {}

    public ApplicationColorTheme Theme
    {
        get => GetValue<ApplicationColorTheme>(nameof(Theme));
        set => SetValue(nameof(Theme), value);
    }
}
