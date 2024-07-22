using Celbridge.Settings;

namespace Celbridge.Legacy.Services;

public interface ISettingsService
{
    IEditorSettings? EditorSettings { get; }
    ProjectSettings? ProjectSettings { get; }
    void SaveProjectSettings(Guid projectId);
    void LoadProjectSettings(Guid projectId);
    void ClearProjectSettings();
}

public class SettingsService : ISettingsService
{
    private const string ProjectSettingsKey = "ProjectSettings";

    public IEditorSettings EditorSettings { get; private set; }
    public ProjectSettings? ProjectSettings { get; private set; }

    public SettingsService(IEditorSettings editorSettings)
    {
        EditorSettings = editorSettings;
    }

    public void LoadProjectSettings(Guid projectId)
    {
        var key = $"{ProjectSettingsKey}_{projectId}";

        try
        {
            var projectSettings = ApplicationData.Current.LocalSettings.Values[key];
            if (projectSettings == null)
            {
                ProjectSettings = null;
            }
            else
            {
                var settingsJson = projectSettings.ToString();
                Guard.IsNotNull(settingsJson);

                ProjectSettings = JsonConvert.DeserializeObject<ProjectSettings>(settingsJson);
            }
        }
#pragma warning disable 0168
        catch (Exception ex)
#pragma warning restore 0168
        {
            // Unable to read the settings (e.g. broken json)
            Debugger.Break();
            ProjectSettings = null;
            ApplicationData.Current.LocalSettings.Values.Remove(key);
        }

#pragma warning disable IDE0074 // Use compound assignment
        if (ProjectSettings == null)
        {
            ProjectSettings = new ProjectSettings()
            {
                ProjectId = projectId
            };
        }
#pragma warning restore IDE0074 // Use compound assignment

        ProjectSettings.PropertyChanged += ProjectSettings_PropertyChanged;
    }

    private void ProjectSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Guard.IsNotNull(ProjectSettings);

        // Todo: Avoid saving multiple times per frame - use a dirty flag and an update tick
        var projectId = ProjectSettings.ProjectId;
        SaveProjectSettings(projectId);
    }

    public void SaveProjectSettings(Guid projectId)
    {
        if (ProjectSettings == null)
        {
            ProjectSettings = new ProjectSettings()
            {
                ProjectId = projectId
            };
        }

        try
        {
            var settingsJson = JsonConvert.SerializeObject(ProjectSettings, Formatting.Indented);
            var key = $"{ProjectSettingsKey}_{ProjectSettings.ProjectId}";
            ApplicationData.Current.LocalSettings.Values[key] = settingsJson;
        }
#pragma warning disable 0168
        catch (Exception _)
#pragma warning restore 0168
        {
            Debugger.Break();
        }
    }

    public void ClearProjectSettings()
    {
        ProjectSettings = null;
    }
}
