using Celbridge.UserInterface;

namespace Celbridge.Settings.Services;

public class EditorSettings : ObservableSettings, IEditorSettings
{
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
        get => GetValue<float>(nameof(ContextPanelWidth), 250);
        set => SetValue(nameof(ContextPanelWidth), value);
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
        get => GetValue<bool>(nameof(IsToolsPanelVisible), true);
        set => SetValue(nameof(IsToolsPanelVisible), value);
    }

    public float ToolsPanelHeight
    {
        get => GetValue<float>(nameof(ToolsPanelHeight), 300 );  // %%% Need to find way of reading height of application window here.
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
}
