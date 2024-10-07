namespace Celbridge.Settings.Services;

public class EditorSettings : ObservableSettings, IEditorSettings
{
    public EditorSettings(ISettingsGroup settingsGroup)
        : base(settingsGroup, nameof(EditorSettings))
    {}

    public bool IsExplorerPanelVisible
    {
        get => GetValue<bool>(nameof(IsExplorerPanelVisible), true);
        set => SetValue(nameof(IsExplorerPanelVisible), value);
    }

    public float ExplorerPanelWidth
    {
        get => GetValue<float>(nameof(ExplorerPanelWidth), 250);
        set => SetValue(nameof(ExplorerPanelWidth), value);
    }

    public bool IsInspectorPanelVisible
    {
        get => GetValue<bool>(nameof(IsInspectorPanelVisible), true);
        set => SetValue(nameof(IsInspectorPanelVisible), value);
    }

    public float InspectorPanelWidth
    {
        get => GetValue<float>(nameof(InspectorPanelWidth), 250);
        set => SetValue(nameof(InspectorPanelWidth), value);
    }

    public bool IsToolsPanelVisible
    {
        get => GetValue<bool>(nameof(IsToolsPanelVisible), false);
        set => SetValue(nameof(IsToolsPanelVisible), value);
    }

    public float ToolsPanelHeight
    {
        get => GetValue<float>(nameof(ToolsPanelHeight), 200);
        set => SetValue(nameof(ToolsPanelHeight), value);
    }

    public float DetailPanelHeight
    {
        get => GetValue<float>(nameof(DetailPanelHeight), 200);
        set => SetValue(nameof(DetailPanelHeight), value);
    }

    public string PreviousNewProjectFolderPath
    {
        get => GetValue<string>(nameof(PreviousNewProjectFolderPath), string.Empty);
        set => SetValue(nameof(PreviousNewProjectFolderPath), value);
    }

    public string PreviousProject
    {
        get => GetValue<string>(nameof(PreviousProject), string.Empty);
        set => SetValue(nameof(PreviousProject), value);
    }

    public List<string> RecentProjects
    {
        get => GetValue<List<string>>(nameof(RecentProjects), new List<string>());
        set => SetValue(nameof(RecentProjects), value);
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
