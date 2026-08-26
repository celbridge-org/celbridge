using Celbridge.UserInterface;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.WebView.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.Views;

/// <summary>
/// The settings surface of a .webview document: a rail of sections covering the document area in place of
/// the page. The same layout the Project Settings editor and the Application Settings dialog present, so a
/// new section is added here the way it is added there.
/// </summary>
public sealed partial class WebViewDocumentSettingsView : UserControl
{
    /// <summary>
    /// The key of the section holding the Home URL, which is where a document with nothing to show has to
    /// open.
    /// </summary>
    public const string HomeSectionKey = "Home";

    private readonly IStringLocalizer _stringLocalizer;

    private WebViewDocumentViewModel? _viewModel;

    // The section to fall back on when the rail reports no selection.
    private SettingsSection? _lastSelectedSection;

    public string ReturnToPageString => _stringLocalizer.GetString("WebView_Settings_ReturnToPage");

    /// <summary>
    /// Raised when the user leaves the settings from the rail footer.
    /// </summary>
    public event EventHandler? ReturnToPageRequested;

    /// <summary>
    /// The key of the section showing, so the document can reopen on the one the user last had open.
    /// Empty until the sections are built.
    /// </summary>
    public string SelectedSectionKey => SectionSwitcher.SelectedSection?.Key ?? string.Empty;

    public WebViewDocumentSettingsView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        InitializeComponent();

        SectionSwitcher.RegisterPropertyChangedCallback(
            SettingsSectionSwitcher.SelectedSectionProperty,
            (_, _) => ApplySelection());
    }

    /// <summary>
    /// Builds the sections over the document's view model, selecting the one the given key names. Called
    /// the first time the settings are shown, so a document that never opens them builds no section views.
    /// </summary>
    public void Initialize(WebViewDocumentViewModel viewModel, string selectedSectionKey)
    {
        if (_viewModel is not null)
        {
            return;
        }

        _viewModel = viewModel;

        var sections = BuildSections(viewModel);
        SectionSwitcher.Sections = sections;

        // An unrecognized or empty key lands on the first section, which is what a new document gets.
        var storedSection = sections.FirstOrDefault(section => section.Key == selectedSectionKey);
        SectionSwitcher.SelectedSection = storedSection ?? sections[0];
    }

    // The sections in rail order. The keys are persisted, so changing one drops the section a returning
    // user had open.
    private List<SettingsSection> BuildSections(WebViewDocumentViewModel viewModel)
    {
        var homeView = new WebViewHomeSectionView
        {
            ViewModel = viewModel
        };

        var appearanceView = new WebViewAppearanceSectionView
        {
            ViewModel = viewModel
        };

        var browsingDataView = new WebViewBrowsingDataSectionView();

        var sections = new List<SettingsSection>
        {
            new(
                HomeSectionKey,
                "bs-house",
                _stringLocalizer.GetString("WebView_Settings_HomeHeader"),
                _stringLocalizer.GetString("WebView_Settings_HomeDescription"),
                homeView),
            new(
                "Appearance",
                "bs-palette",
                _stringLocalizer.GetString("WebView_Settings_AppearanceHeader"),
                _stringLocalizer.GetString("WebView_Settings_AppearanceDescription"),
                appearanceView),
            new(
                "BrowsingData",
                "bs-clock-history",
                _stringLocalizer.GetString("WebView_Settings_BrowsingDataHeader"),
                _stringLocalizer.GetString("WebView_Settings_BrowsingDataDescription"),
                browsingDataView),
        };

        return sections;
    }

    // The switcher writes the clicked section but leaves the checked state of the rail rows to its owner,
    // so exactly one row carries it.
    private void ApplySelection()
    {
        var selectedSection = SectionSwitcher.SelectedSection;
        if (selectedSection is null)
        {
            // Nothing in the rail clears the selection, but the property is public: hold the invariant that
            // a section is always showing rather than leaving it to callers.
            SectionSwitcher.SelectedSection = _lastSelectedSection;
            return;
        }

        var sections = SectionSwitcher.Sections;
        if (sections is null)
        {
            return;
        }

        foreach (var section in sections)
        {
            section.IsSelected = ReferenceEquals(section, selectedSection);
        }

        _lastSelectedSection = selectedSection;
    }

    private void ReturnToPageButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnToPageRequested?.Invoke(this, EventArgs.Empty);
    }
}
