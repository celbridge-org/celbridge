using Celbridge.UserInterface.Views.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WebView.ViewModels;

/// <summary>
/// Coordinates the Web View settings surface's section rail. The sections are views over the document's own
/// view model, so this holds only which one is showing.
/// </summary>
public partial class WebViewDocumentSettingsViewModel : ObservableObject
{
    // The section to fall back on when the rail reports no selection.
    private SettingsSection? _lastSelectedSection;

    [ObservableProperty]
    private IReadOnlyList<SettingsSection> _sections = Array.Empty<SettingsSection>();

    [ObservableProperty]
    private SettingsSection? _selectedSection;

    /// <summary>
    /// The key of the section showing, so the document can reopen on the one the user last had open. Empty
    /// until the sections are built.
    /// </summary>
    public string SelectedSectionKey => SelectedSection?.Key ?? string.Empty;

    /// <summary>
    /// Supplies the sections to show, in rail order, and selects the one the given key names. Called by the
    /// surface, which is the only place that can build each section's content.
    /// </summary>
    public void InitializeSections(IReadOnlyList<SettingsSection> sections, string selectedSectionKey)
    {
        Sections = sections;

        // An unrecognized or empty key lands on the first section, which is what a new document gets.
        var storedSection = sections.FirstOrDefault(section => section.Key == selectedSectionKey);

        SelectedSection = storedSection ?? sections.FirstOrDefault();
    }

    /// <summary>
    /// Shows the section the given key names. Does nothing until the sections are built, or for a key none
    /// of them carries.
    /// </summary>
    public void SelectSection(string sectionKey)
    {
        var selectedSection = Sections.FirstOrDefault(section => section.Key == sectionKey);
        if (selectedSection is null)
        {
            return;
        }

        SelectedSection = selectedSection;
    }

    partial void OnSelectedSectionChanged(SettingsSection? value)
    {
        if (value is null)
        {
            // Nothing in the rail clears the selection, but the property is public: hold the invariant that
            // a section is always showing rather than leaving it to callers.
            SelectedSection = _lastSelectedSection;
            return;
        }

        foreach (var section in Sections)
        {
            section.IsSelected = ReferenceEquals(section, value);
        }

        _lastSelectedSection = value;
    }
}
