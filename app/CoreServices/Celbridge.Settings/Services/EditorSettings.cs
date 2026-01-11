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

    public LayoutMode LayoutMode
    {
        get => GetValue<LayoutMode>(nameof(LayoutMode), LayoutMode.Windowed);
        set => SetValue(nameof(LayoutMode), value);
    }

    public bool FullscreenPreContextPanelVisible
    {
        get => GetValue<bool>(nameof(FullscreenPreContextPanelVisible), true);
        set => SetValue(nameof(FullscreenPreContextPanelVisible), value);
    }

    public bool FullscreenPreInspectorPanelVisible
    {
        get => GetValue<bool>(nameof(FullscreenPreInspectorPanelVisible), true);
        set => SetValue(nameof(FullscreenPreInspectorPanelVisible), value);
    }

    public bool FullscreenPreConsolePanelVisible
    {
        get => GetValue<bool>(nameof(FullscreenPreConsolePanelVisible), true);
        set => SetValue(nameof(FullscreenPreConsolePanelVisible), value);
    }

    public void ResetPanelState()
    {
        IsContextPanelVisible = true;
        ContextPanelWidth = DefaultContextPanelWidth;
        IsInspectorPanelVisible = true;
        InspectorPanelWidth = DefaultInspectorPanelWidth;
        IsConsolePanelVisible = true;
        ConsolePanelHeight = DefaultConsolePanelHeight;
    }

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
}
