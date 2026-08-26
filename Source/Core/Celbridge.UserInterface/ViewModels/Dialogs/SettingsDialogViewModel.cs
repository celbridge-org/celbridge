using Celbridge.Settings;
using Celbridge.UserInterface.Views.Controls;

namespace Celbridge.UserInterface.ViewModels.Dialogs;

/// <summary>
/// Coordinates the settings dialog's category rail. The categories themselves are self-contained views
/// with their own view models, so this holds only which one is showing and where that is remembered.
/// </summary>
public partial class SettingsDialogViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    // Persistence is enabled only once the restore has run, so selecting the remembered category does not
    // immediately rewrite it.
    private bool _sectionPersistenceEnabled;

    // The category to fall back on when the rail reports no selection.
    private SettingsSection? _lastSelectedSection;

    [ObservableProperty]
    private IReadOnlyList<SettingsSection> _sections = Array.Empty<SettingsSection>();

    [ObservableProperty]
    private SettingsSection? _selectedSection;

    public SettingsDialogViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Supplies the categories to show, in rail order, and selects the requested one, falling back to the
    /// one the user last had open. Called by the dialog, which is the only place that can build each
    /// category's content. Persistence stays off over the selection, so opening on a requested category
    /// does not displace the one the user chose for themselves.
    /// </summary>
    public void InitializeSections(IReadOnlyList<SettingsSection> sections, string requestedSectionKey)
    {
        Guard.IsTrue(sections.Count > 0);

        _sectionPersistenceEnabled = false;
        Sections = sections;

        var requestedSection = sections.FirstOrDefault(section => section.Key == requestedSectionKey);

        // An unrecognized or empty key lands on the first category, which is what a new installation gets.
        var storedKey = _settingsService.Get(SettingCatalog.Application.SettingsDialogSelectedSection);
        var storedSection = sections.FirstOrDefault(section => section.Key == storedKey);

        SelectedSection = requestedSection ?? storedSection ?? sections[0];

        _sectionPersistenceEnabled = true;
    }

    partial void OnSelectedSectionChanged(SettingsSection? value)
    {
        if (value is null)
        {
            // Nothing in the rail clears the selection, but the property is public: hold the invariant that
            // a category is always showing rather than leaving it to callers.
            SelectedSection = _lastSelectedSection;
            return;
        }

        foreach (var section in Sections)
        {
            section.IsSelected = ReferenceEquals(section, value);
        }

        _lastSelectedSection = value;

        if (!_sectionPersistenceEnabled)
        {
            return;
        }

        // The key rather than the position, so inserting or reordering a category does not land a
        // returning user on a different one.
        _settingsService.Set(SettingCatalog.Application.SettingsDialogSelectedSection, value.Key);
    }
}
