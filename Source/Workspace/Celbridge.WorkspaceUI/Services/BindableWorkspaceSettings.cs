using System.ComponentModel;
using System.Runtime.CompilerServices;
using Celbridge.Settings;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Presentation facade over ISettingsService for the Workspace-scope setting
/// descriptors: each property reads and writes its descriptor through the Get and
/// Set helpers, which raise PropertyChanged for the calling property. The
/// descriptors remain the source of truth; this type exists so views can bind to
/// named per-project panel, search, and editor state. Programmatic access goes
/// through ISettingsService; this facade is only for the views that bind.
/// </summary>
public sealed class BindableWorkspaceSettings : IBindableWorkspaceSettings
{
    private readonly ISettingsService _settings;

    public event PropertyChangedEventHandler? PropertyChanged;

    public BindableWorkspaceSettings(ISettingsService settings)
    {
        _settings = settings;
    }

    public LayoutRegion PreferredRegionVisibility
    {
        get => Get(SettingCatalog.Layout.PreferredRegionVisibility);
        set => Set(SettingCatalog.Layout.PreferredRegionVisibility, value);
    }

    public float PrimaryPanelWidth
    {
        get => Get(SettingCatalog.Layout.PrimaryPanelWidth);
        set => Set(SettingCatalog.Layout.PrimaryPanelWidth, value);
    }

    public float SecondaryPanelWidth
    {
        get => Get(SettingCatalog.Layout.SecondaryPanelWidth);
        set => Set(SettingCatalog.Layout.SecondaryPanelWidth, value);
    }

    public float ConsolePanelHeight
    {
        get => Get(SettingCatalog.Layout.ConsolePanelHeight);
        set => Set(SettingCatalog.Layout.ConsolePanelHeight, value);
    }

    public float DetailPanelHeight
    {
        get => Get(SettingCatalog.Layout.DetailPanelHeight);
        set => Set(SettingCatalog.Layout.DetailPanelHeight, value);
    }

    public bool IsConsoleMaximized
    {
        get => Get(SettingCatalog.Layout.IsConsoleMaximized);
        set => Set(SettingCatalog.Layout.IsConsoleMaximized, value);
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
