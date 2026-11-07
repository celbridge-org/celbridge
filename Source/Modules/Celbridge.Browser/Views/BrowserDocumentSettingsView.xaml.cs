using Celbridge.UserInterface;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.Browser.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Browser.Views;

/// <summary>
/// The settings surface of a .browser document: a rail of sections covering the document area in place of
/// the page. The same layout the Project Settings editor and the Application Settings dialog present, so a
/// new section is added here the way it is added there.
/// </summary>
public sealed partial class BrowserDocumentSettingsView : UserControl
{
    /// <summary>
    /// The key of the section showing the bookmarks. Edit Bookmarks opens this section, from the bookmark list and from
    /// the empty page.
    /// </summary>
    public const string BookmarksSectionKey = "Bookmarks";

    private readonly IStringLocalizer _stringLocalizer;
    private readonly IIconService _iconService;

    private BrowserDocumentViewModel? _documentViewModel;

    public BrowserDocumentSettingsViewModel ViewModel { get; }

    public string ReturnToPageString => _stringLocalizer.GetString("Browser_Settings_ReturnToPage");

    /// <summary>
    /// Raised when the user leaves the settings with the close button over the section heading.
    /// </summary>
    public event EventHandler? ReturnToPageRequested;

    /// <summary>
    /// The key of the section showing, so the document can reopen on the one the user last had open.
    /// Empty until the sections are built.
    /// </summary>
    public string SelectedSectionKey => ViewModel.SelectedSectionKey;

    public BrowserDocumentSettingsView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _iconService = ServiceLocator.AcquireService<IIconService>();

        // The view model backs x:Bind paths, so it must exist before InitializeComponent evaluates them.
        ViewModel = ServiceLocator.AcquireService<BrowserDocumentSettingsViewModel>();

        InitializeComponent();

        SectionSwitcher.CloseRequested += SectionSwitcher_CloseRequested;
    }

    /// <summary>
    /// Builds the sections over the document's view model, selecting the one the given key names. Called
    /// the first time the settings are shown, so a document that never opens them builds no section views.
    /// </summary>
    public void Initialize(BrowserDocumentViewModel viewModel, string selectedSectionKey)
    {
        if (_documentViewModel is not null)
        {
            return;
        }

        _documentViewModel = viewModel;

        var sections = BuildSections(viewModel);

        ViewModel.InitializeSections(sections, selectedSectionKey);
    }

    /// <summary>
    /// Shows the section the given key names. Does nothing until the sections are built, or for a key none
    /// of them carries.
    /// </summary>
    public void SelectSection(string sectionKey)
    {
        ViewModel.SelectSection(sectionKey);
    }

    /// <summary>
    /// Gives the keyboard to the section rail, and reports whether it took it.
    /// </summary>
    public bool FocusRail()
    {
        return SectionSwitcher.FocusRail();
    }

    // The sections in rail order. The keys are persisted, so changing one drops the section a returning
    // user had open.
    private List<SettingsSection> BuildSections(BrowserDocumentViewModel viewModel)
    {
        var bookmarksView = new BrowserBookmarksSectionView
        {
            ViewModel = viewModel
        };

        var browsingDataView = new BrowserBrowsingDataSectionView();

        var sections = new List<SettingsSection>
        {
            new(
                BookmarksSectionKey,
                _iconService.GetIconName(IconSymbol.Star),
                _stringLocalizer.GetString("Browser_Settings_BookmarksHeader"),
                _stringLocalizer.GetString("Browser_Settings_BookmarksDescription"),
                bookmarksView),
            new(
                "BrowsingData",
                "bs-clock-history",
                _stringLocalizer.GetString("Browser_Settings_BrowsingDataHeader"),
                _stringLocalizer.GetString("Browser_Settings_BrowsingDataDescription"),
                browsingDataView),
        };

        return sections;
    }

    private void SectionSwitcher_CloseRequested(object? sender, EventArgs e)
    {
        ReturnToPageRequested?.Invoke(this, EventArgs.Empty);
    }
}
