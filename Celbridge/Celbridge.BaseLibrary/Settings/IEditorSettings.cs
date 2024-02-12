namespace Celbridge.BaseLibrary.Settings;

/// <summary>
/// Manage persitent user settings via named setting containers.
/// </summary>
public interface IEditorSettings
{
    ApplicationColorTheme Theme { get; set; }

    void Reset();
}
