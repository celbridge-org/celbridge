using System.ComponentModel;
using System.Runtime.CompilerServices;
using Celbridge.Settings;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Presentation facade over ISettingsService for the Workspace-scope setting descriptors,
/// letting views bind to named per-project panel, search, and editor state.
/// </summary>
public sealed class BindableWorkspaceSettings : IBindableWorkspaceSettings
{
    private readonly ISettingsService _settings;

    public event PropertyChangedEventHandler? PropertyChanged;

    public BindableWorkspaceSettings(ISettingsService settings)
    {
        _settings = settings;
    }

    public WorkspaceSurface PreferredSurfaceVisibility
    {
        get => Get(SettingCatalog.Layout.PreferredSurfaceVisibility);
        set => Set(SettingCatalog.Layout.PreferredSurfaceVisibility, value);
    }

    public float UtilityPanelWidth
    {
        get => Get(SettingCatalog.Layout.UtilityPanelWidth);
        set => Set(SettingCatalog.Layout.UtilityPanelWidth, value);
    }

    public float SideAreaWidth
    {
        get => Get(SettingCatalog.Layout.SideAreaWidth);
        set => Set(SettingCatalog.Layout.SideAreaWidth, value);
    }

    public float BottomAreaHeight
    {
        get => Get(SettingCatalog.Layout.BottomAreaHeight);
        set => Set(SettingCatalog.Layout.BottomAreaHeight, value);
    }

    public bool SearchMatchCase
    {
        get => Get(SettingCatalog.Search.MatchCase);
        set => Set(SettingCatalog.Search.MatchCase, value);
    }

    public bool SearchWholeWord
    {
        get => Get(SettingCatalog.Search.WholeWord);
        set => Set(SettingCatalog.Search.WholeWord, value);
    }

    public bool ReplaceMode
    {
        get => Get(SettingCatalog.Search.ReplaceMode);
        set => Set(SettingCatalog.Search.ReplaceMode, value);
    }

    public string PreviousNewFileExtension
    {
        get => Get(SettingCatalog.Editor.PreviousNewFileExtension);
        set => Set(SettingCatalog.Editor.PreviousNewFileExtension, value);
    }

    private T Get<T>(SettingDescriptor<T> descriptor) where T : notnull
    {
        return _settings.Get(descriptor);
    }

    // [CallerMemberName] resolves to the property whose setter called this, so the
    // change notification targets that property without a name lookup table.
    private void Set<T>(SettingDescriptor<T> descriptor, T value, [CallerMemberName] string? propertyName = null) where T : notnull
    {
        // During an active project a write always has a store. This guard is the boundary
        // backstop: panel SizeChanged can still fire on teardown after the store is unloaded,
        // where dropping the transient layout value is correct rather than throwing.
        if (!_settings.IsScopeAvailable(SettingScope.Workspace))
        {
            return;
        }

        _settings.Set(descriptor, value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
