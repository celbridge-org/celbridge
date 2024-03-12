using Celbridge.BaseLibrary.Settings;

namespace Celbridge.Services.Settings;

public class EditorSettings : ObservableSettings, IEditorSettings
{
    public EditorSettings(ISettingsGroup settingsGroup)
        : base(settingsGroup, nameof(EditorSettings))
    {}

    public bool LeftPanelExpanded
    {
        get => GetValue<bool>(nameof(LeftPanelExpanded), true);
        set => SetValue(nameof(LeftPanelExpanded), value);
    }

    public float LeftPanelWidth
    {
        get => GetValue<float>(nameof(LeftPanelWidth), 250);
        set => SetValue(nameof(LeftPanelWidth), value);
    }

    public bool RightPanelExpanded
    {
        get => GetValue<bool>(nameof(RightPanelExpanded), true);
        set => SetValue(nameof(RightPanelExpanded), value);
    }

    public float RightPanelWidth
    {
        get => GetValue<float>(nameof(RightPanelWidth), 250);
        set => SetValue(nameof(RightPanelWidth), value);
    }

    public bool BottomPanelExpanded
    {
        get => GetValue<bool>(nameof(BottomPanelExpanded), true);
        set => SetValue(nameof(BottomPanelExpanded), value);
    }

    public float BottomPanelHeight
    {
        get => GetValue<float>(nameof(BottomPanelHeight), 200);
        set => SetValue(nameof(BottomPanelHeight), value);
    }

    public float DetailPanelHeight
    {
        get => GetValue<float>(nameof(DetailPanelHeight), 200);
        set => SetValue(nameof(DetailPanelHeight), value);
    }

    public string PreviousNewProjectFolder
    {
        get => GetValue<string>(nameof(PreviousNewProjectFolder), string.Empty);
        set => SetValue(nameof(PreviousNewProjectFolder), value);
    }

    public string PreviousActiveProjectPath
    {
        get => GetValue<string>(nameof(PreviousActiveProjectPath), string.Empty);
        set => SetValue(nameof(PreviousActiveProjectPath), value);
    }

    public List<string> PreviousOpenDocuments
    {
        get => GetValue<List<string>>(nameof(PreviousOpenDocuments), new());
        set => SetValue(nameof(PreviousOpenDocuments), value);
    }

    public string OpenAIKey
    {
        get => GetValue<string>(nameof(OpenAIKey), string.Empty);
        set => SetValue(nameof(OpenAIKey), value);
    }

    public string SheetsAPIKey
    {
        get => GetValue<string>(nameof(SheetsAPIKey), string.Empty);
        set => SetValue(nameof(SheetsAPIKey), value);
    }
}
