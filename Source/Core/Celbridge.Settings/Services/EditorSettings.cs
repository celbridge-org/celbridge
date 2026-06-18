using System.ComponentModel;
using System.Runtime.CompilerServices;
using Celbridge.Workspace;

namespace Celbridge.Settings.Services;

/// <summary>
/// Presentation facade over ISettingsService for binding ergonomics: each property
/// reads and writes a descriptor in Setting through the Get and Set helpers, which
/// raise PropertyChanged for the calling property. The descriptors remain the
/// source of truth; this type exists so views can bind to named properties.
/// </summary>
public sealed class EditorSettings : IEditorSettings
{
    private readonly ISettingsService _settings;

    public event PropertyChangedEventHandler? PropertyChanged;

    public EditorSettings(ISettingsService settings)
    {
        _settings = settings;
    }

    public void Reset()
    {
        foreach (var descriptor in Setting.All)
        {
            _settings.Reset(descriptor);
        }

        // An empty property name signals that every bound property may have changed.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public string PreviousNewProjectFolderPath
    {
        get => Get(Setting.Project.PreviousNewProjectFolderPath);
        set => Set(Setting.Project.PreviousNewProjectFolderPath, value);
    }

    public string PreviousProject
    {
        get => Get(Setting.Project.PreviousProject);
        set => Set(Setting.Project.PreviousProject, value);
    }

    public List<string> RecentProjects
    {
        // Return a fresh list rather than the descriptor's shared default, so a
        // caller that mutates the result before writing it back cannot corrupt
        // the default for the next unconfigured read.
        get => new List<string>(Get(Setting.Project.RecentProjects));
        set => Set(Setting.Project.RecentProjects, value);
    }

    public string PreviousNewFileExtension
    {
        get => Get(Setting.Editor.PreviousNewFileExtension);
        set => Set(Setting.Editor.PreviousNewFileExtension, value);
    }

    public string PreviousNewProjectTemplateName
    {
        get => Get(Setting.Project.PreviousNewProjectTemplateName);
        set => Set(Setting.Project.PreviousNewProjectTemplateName, value);
    }

    public bool UsePreferredWindowGeometry
    {
        get => Get(Setting.Window.UsePreferredGeometry);
        set => Set(Setting.Window.UsePreferredGeometry, value);
    }

    public bool IsWindowMaximized
    {
        get => Get(Setting.Window.IsMaximized);
        set => Set(Setting.Window.IsMaximized, value);
    }

    public int PreferredWindowX
    {
        get => Get(Setting.Window.PreferredX);
        set => Set(Setting.Window.PreferredX, value);
    }

    public int PreferredWindowY
    {
        get => Get(Setting.Window.PreferredY);
        set => Set(Setting.Window.PreferredY, value);
    }

    public int PreferredWindowWidth
    {
        get => Get(Setting.Window.PreferredWidth);
        set => Set(Setting.Window.PreferredWidth, value);
    }

    public int PreferredWindowHeight
    {
        get => Get(Setting.Window.PreferredHeight);
        set => Set(Setting.Window.PreferredHeight, value);
    }

    public LayoutRegion PreferredRegionVisibility
    {
        get => Get(Setting.Layout.PreferredRegionVisibility);
        set => Set(Setting.Layout.PreferredRegionVisibility, value);
    }

    public float PrimaryPanelWidth
    {
        get => Get(Setting.Layout.PrimaryPanelWidth);
        set => Set(Setting.Layout.PrimaryPanelWidth, value);
    }

    public float SecondaryPanelWidth
    {
        get => Get(Setting.Layout.SecondaryPanelWidth);
        set => Set(Setting.Layout.SecondaryPanelWidth, value);
    }

    public float ConsolePanelHeight
    {
        get => Get(Setting.Layout.ConsolePanelHeight);
        set => Set(Setting.Layout.ConsolePanelHeight, value);
    }

    public float DetailPanelHeight
    {
        get => Get(Setting.Layout.DetailPanelHeight);
        set => Set(Setting.Layout.DetailPanelHeight, value);
    }

    public bool IsConsoleMaximized
    {
        get => Get(Setting.Layout.IsConsoleMaximized);
        set => Set(Setting.Layout.IsConsoleMaximized, value);
    }

    public ApplicationColorTheme Theme
    {
        get => Get(Setting.Application.Theme);
        set => Set(Setting.Application.Theme, value);
    }

    public string WorkshopUrl
    {
        get => Get(Setting.Workshop.Url);
        set => Set(Setting.Workshop.Url, value);
    }

    public string WorkshopAuthor
    {
        get => Get(Setting.Workshop.Author);
        set => Set(Setting.Workshop.Author, value);
    }

    public bool SearchMatchCase
    {
        get => Get(Setting.Search.MatchCase);
        set => Set(Setting.Search.MatchCase, value);
    }

    public bool SearchWholeWord
    {
        get => Get(Setting.Search.WholeWord);
        set => Set(Setting.Search.WholeWord, value);
    }

    public bool ReplaceMode
    {
        get => Get(Setting.Search.ReplaceMode);
        set => Set(Setting.Search.ReplaceMode, value);
    }

    private T Get<T>(SettingDescriptor<T> descriptor) where T : notnull
    {
        return _settings.Get(descriptor);
    }

    // [CallerMemberName] resolves to the property whose setter called this, so the
    // change notification targets that property without a name lookup table.
    private void Set<T>(SettingDescriptor<T> descriptor, T value, [CallerMemberName] string? propertyName = null) where T : notnull
    {
        _settings.Set(descriptor, value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
