using Celbridge.Settings;

namespace Celbridge.UserInterface.ViewModels.Dialogs;

/// <summary>
/// One category in the settings dialog's rail: the stable key it is persisted under, the label and
/// description shown for it, and the content shown while it is selected.
/// </summary>
public sealed partial record SettingsSection(string Key, string Label, string Description, object Content);

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

    public IReadOnlyList<SettingsSection> Sections { get; private set; } = Array.Empty<SettingsSection>();

    [ObservableProperty]
    private SettingsSection? _selectedSection;

    public SettingsDialogViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Supplies the categories to show, in rail order, and selects the one the user last had open. Called
    /// by the dialog, which is the only place that can build each category's content.
    /// </summary>
    public void InitializeSections(IReadOnlyList<SettingsSection> sections)
    {
        Guard.IsTrue(sections.Count > 0);

        _sectionPersistenceEnabled = false;
        Sections = sections;

        // An unrecognized or empty key lands on the first category, which is what a new installation gets.
        var storedKey = _settingsService.Get(SettingCatalog.Application.SettingsDialogSelectedSection);
        var storedSection = sections.FirstOrDefault(section => section.Key == storedKey);

        SelectedSection = storedSection ?? sections[0];

        _sectionPersistenceEnabled = true;
    }

    partial void OnSelectedSectionChanged(SettingsSection? value)
    {
        if (value is null
            || !_sectionPersistenceEnabled)
        {
            return;
        }

        // The key rather than the position, so inserting or reordering a category does not land a
        // returning user on a different one.
        _settingsService.Set(SettingCatalog.Application.SettingsDialogSelectedSection, value.Key);
    }
}
