using System.ComponentModel;
using System.Runtime.CompilerServices;
using Celbridge.Settings;
using Celbridge.Workspace;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Presentation facade over ISettingsService for the Workspace-scope setting
/// descriptors: each property reads and writes its descriptor through the Get and
/// Set helpers, which raise PropertyChanged for the calling property. The
/// descriptors remain the source of truth; this type exists so views can bind to
/// named per-project panel, search, and editor state. It is the workspace-scope
/// peer of EditorSettings.
/// </summary>
public sealed class WorkspaceSettings : IWorkspaceSettings
{
    private readonly ISettingsService _settings;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceSettings(ISettingsService settings)
    {
        _settings = settings;
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

    public string PreviousNewFileExtension
    {
        get => Get(Setting.Editor.PreviousNewFileExtension);
        set => Set(Setting.Editor.PreviousNewFileExtension, value);
    }

    private T Get<T>(SettingDescriptor<T> descriptor) where T : notnull
    {
        return _settings.Get(descriptor);
    }

    // [CallerMemberName] resolves to the property whose setter called this, so the
    // change notification targets that property without a name lookup table.
    private void Set<T>(SettingDescriptor<T> descriptor, T value, [CallerMemberName] string? propertyName = null) where T : notnull
    {
        // The store is brought online before the panels bind (see
        // WorkspacePage.WorkspacePage_Loaded), so during an active project a write
        // always has a store. This guard is the boundary backstop: panel
        // SizeChanged can still fire on teardown after the store is unloaded, where
        // dropping the transient layout value is correct rather than throwing.
        if (!_settings.IsScopeAvailable(SettingScope.Workspace))
        {
            return;
        }

        _settings.Set(descriptor, value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
