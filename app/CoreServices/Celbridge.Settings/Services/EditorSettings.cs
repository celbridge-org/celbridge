namespace Celbridge.Settings.Services;

public class EditorSettings : ObservableSettings, IEditorSettings
{
    private const float DefaultContextPanelWidth = 300f;
    private const float DefaultInspectorPanelWidth = 300f;
    private const float DefaultConsolePanelHeight = 350f;
    private const float DefaultDetailPanelHeight = 250f;

    public EditorSettings(ISettingsGroup settingsGroup)
        : base(settingsGroup, nameof(EditorSettings))
    {}

    public bool IsContextPanelVisible
    {
        get => GetValue<bool>(nameof(IsContextPanelVisible), true);
        set => SetValue(nameof(IsContextPanelVisible), value);
    }

    public float ContextPanelWidth
    {
        get => GetValue<float>(nameof(ContextPanelWidth), DefaultContextPanelWidth);
        set => SetValue(nameof(ContextPanelWidth), value);
    }

    public bool IsInspectorPanelVisible
    {
        get => GetValue<bool>(nameof(IsInspectorPanelVisible), true);
        set => SetValue(nameof(IsInspectorPanelVisible), value);
    }

    public float InspectorPanelWidth
    {
        get => GetValue<float>(nameof(InspectorPanelWidth), DefaultInspectorPanelWidth);
        set => SetValue(nameof(InspectorPanelWidth), value);
    }

    public bool IsConsolePanelVisible
    {
        get => GetValue<bool>(nameof(IsConsolePanelVisible), true);
        set => SetValue(nameof(IsConsolePanelVisible), value);
    }

    public float ConsolePanelHeight
    {
        get => GetValue<float>(nameof(ConsolePanelHeight), DefaultConsolePanelHeight);
        set => SetValue(nameof(ConsolePanelHeight), value);
    }

    public float DetailPanelHeight
    {
        get => GetValue<float>(nameof(DetailPanelHeight), DefaultDetailPanelHeight);
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

    public ApplicationColorTheme Theme 
    {
        get => GetValue<ApplicationColorTheme>(nameof(Theme), ApplicationColorTheme.System);
        set => SetValue(nameof(Theme), value);
    }

    public bool IsWindowMaximized
    {
        get => GetValue<bool>(nameof(IsWindowMaximized), false);
        set => SetValue(nameof(IsWindowMaximized), value);
    }

    public int WindowX
    {
        get => GetValue<int>(nameof(WindowX), -1);
        set => SetValue(nameof(WindowX), value);
    }

    public int WindowY
    {
        get => GetValue<int>(nameof(WindowY), -1);
        set => SetValue(nameof(WindowY), value);
    }

    public int WindowWidth
    {
        get => GetValue<int>(nameof(WindowWidth), 1200);
        set => SetValue(nameof(WindowWidth), value);
    }

    public int WindowHeight
    {
        get => GetValue<int>(nameof(WindowHeight), 800);
        set => SetValue(nameof(WindowHeight), value);
    }

    public string PreviousNewFileExtension
    {
        get => GetValue<string>(nameof(PreviousNewFileExtension), ".py");
        set => SetValue(nameof(PreviousNewFileExtension), value);
    }

    public bool IsZenModeActive
    {
        get => GetValue<bool>(nameof(IsZenModeActive), false);
        set => SetValue(nameof(IsZenModeActive), value);
    }

    public bool ZenModePreContextPanelVisible
    {
        get => GetValue<bool>(nameof(ZenModePreContextPanelVisible), true);
        set => SetValue(nameof(ZenModePreContextPanelVisible), value);
    }

    public bool ZenModePreInspectorPanelVisible
    {
        get => GetValue<bool>(nameof(ZenModePreInspectorPanelVisible), true);
        set => SetValue(nameof(ZenModePreInspectorPanelVisible), value);
    }

    public bool ZenModePreConsolePanelVisible
    {
        get => GetValue<bool>(nameof(ZenModePreConsolePanelVisible), true);
        set => SetValue(nameof(ZenModePreConsolePanelVisible), value);
    }

    public void ResetPanelLayout()
    {
        IsContextPanelVisible = true;
        ContextPanelWidth = DefaultContextPanelWidth;
        IsInspectorPanelVisible = true;
        InspectorPanelWidth = DefaultInspectorPanelWidth;
        IsConsolePanelVisible = true;
        ConsolePanelHeight = DefaultConsolePanelHeight;
    }
}
