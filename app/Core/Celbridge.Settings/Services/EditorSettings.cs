using Celbridge.UserInterface;

namespace Celbridge.Settings.Services;

public class EditorSettings : ObservableSettings, IEditorSettings
{
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

    public string PreviousNewFileExtension
    {
        get => GetValue<string>(nameof(PreviousNewFileExtension), ".py");
        set => SetValue(nameof(PreviousNewFileExtension), value);
    }

    public string PreviousNewProjectTemplateName
    {
        get => GetValue<string>(nameof(PreviousNewProjectTemplateName), string.Empty);
        set => SetValue(nameof(PreviousNewProjectTemplateName), value);
    }

    public bool UsePreferredWindowGeometry
    {
        get => GetValue<bool>(nameof(UsePreferredWindowGeometry), false);
        set => SetValue(nameof(UsePreferredWindowGeometry), value);
    }

    public bool IsWindowMaximized
    {
        get => GetValue<bool>(nameof(IsWindowMaximized), false);
        set => SetValue(nameof(IsWindowMaximized), value);
    }

    public int PreferredWindowX
    {
        get => GetValue<int>(nameof(PreferredWindowX), 0);
        set => SetValue(nameof(PreferredWindowX), value);
    }

    public int PreferredWindowY
    {
        get => GetValue<int>(nameof(PreferredWindowY), 0);
        set => SetValue(nameof(PreferredWindowY), value);
    }

    public int PreferredWindowWidth
    {
        get => GetValue<int>(nameof(PreferredWindowWidth), 0);
        set => SetValue(nameof(PreferredWindowWidth), value);
    }

    public int PreferredWindowHeight
    {
        get => GetValue<int>(nameof(PreferredWindowHeight), 0);
        set => SetValue(nameof(PreferredWindowHeight), value);
    }

    public PanelRegion PreferredPanelVisibility
    {
        get => GetValue<PanelRegion>(nameof(PreferredPanelVisibility), PanelRegion.All);
        set => SetValue(nameof(PreferredPanelVisibility), value);
    }

    public float PrimaryPanelWidth
    {
        get => GetValue<float>(nameof(PrimaryPanelWidth), UserInterfaceConstants.PrimaryPanelWidth);
        set => SetValue(nameof(PrimaryPanelWidth), value);
    }

    public float SecondaryPanelWidth
    {
        get => GetValue<float>(nameof(SecondaryPanelWidth), UserInterfaceConstants.SecondaryPanelWidth);
        set => SetValue(nameof(SecondaryPanelWidth), value);
    }

    public float ConsolePanelHeight
    {
        get => GetValue<float>(nameof(ConsolePanelHeight), UserInterfaceConstants.ConsolePanelHeight);
        set => SetValue(nameof(ConsolePanelHeight), value);
    }

    public float DetailPanelHeight
    {
        get => GetValue<float>(nameof(DetailPanelHeight), UserInterfaceConstants.DetailPanelHeight);
        set => SetValue(nameof(DetailPanelHeight), value);
    }

    public bool IsConsoleMaximized
    {
        get => GetValue<bool>(nameof(IsConsoleMaximized), false);
        set => SetValue(nameof(IsConsoleMaximized), value);
    }

    public float RestoreConsoleHeight
    {
        get => GetValue<float>(nameof(RestoreConsoleHeight), UserInterfaceConstants.ConsolePanelHeight);
        set => SetValue(nameof(RestoreConsoleHeight), value);
    }

    public ApplicationColorTheme Theme
    {
        get => GetValue<ApplicationColorTheme>(nameof(Theme), ApplicationColorTheme.System);
        set => SetValue(nameof(Theme), value);
    }

    public bool SearchMatchCase
    {
        get => GetValue<bool>(nameof(SearchMatchCase), false);
        set => SetValue(nameof(SearchMatchCase), value);
    }

    public bool SearchWholeWord
    {
        get => GetValue<bool>(nameof(SearchWholeWord), false);
        set => SetValue(nameof(SearchWholeWord), value);
    }
}
