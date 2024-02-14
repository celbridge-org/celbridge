using Celbridge.BaseLibrary.Settings;
using Windows.Storage;

namespace Celbridge.CommonServices.Settings;

public class EditorSettings : ObservableSettings, IEditorSettings
{
    public EditorSettings(ISettingsContainer settingsContainer)
        : base(settingsContainer, nameof(EditorSettings))
    {
        PropertyChanged += EditorSettings_PropertyChanged;
    }

    private void EditorSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Theme))
        {
            // Values will be null here when this code is run as a unit test.
            if (ApplicationData.Current.LocalSettings.Values != null)
            {
                // Store a duplicate of the Theme value in ApplicationData.Current.LocalSettings.
                // We need to access this setting in the application constructor, which can't access settings stored
                // ApplicationData.Current.LocalSettings.Containers
                ApplicationData.Current.LocalSettings.Values[nameof(Theme)] = Theme.ToString();
            }
        }
    }

    public override void Reset()
    {
        base.Reset();

        // Values will be null here when this code is run as a unit test.
        if (ApplicationData.Current.LocalSettings.Values != null)
        {
            // Reset the duplicate Theme setting stored above
            ApplicationData.Current.LocalSettings.Values.Remove(nameof(Theme));
        }
    }

    public ApplicationColorTheme Theme
    {
        get => GetValue<ApplicationColorTheme>(nameof(Theme));
        set => SetValue(nameof(Theme), value);
    }

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
