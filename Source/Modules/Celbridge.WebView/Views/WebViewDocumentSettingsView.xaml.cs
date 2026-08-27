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

    /// <summary>
    /// The key of the section holding the bookmarks, which the bookmarks bar sends the user to.
    /// </summary>
    public const string BookmarksSectionKey = "Bookmarks";

    private readonly IStringLocalizer _stringLocalizer;

    private WebViewDocumentViewModel? _documentViewModel;

    public WebViewDocumentSettingsViewModel ViewModel { get; }

    public string ReturnToPageString => _stringLocalizer.GetString("WebView_Settings_ReturnToPage");

    /// <summary>
    /// Raised when the user leaves the settings from the rail footer.
    /// </summary>
    public event EventHandler? ReturnToPageRequested;

    /// <summary>
    /// The key of the section showing, so the document can reopen on the one the user last had open.
    /// Empty until the sections are built.
    /// </summary>
    public string SelectedSectionKey => ViewModel.SelectedSectionKey;

    public WebViewDocumentSettingsView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        // The view model backs x:Bind paths, so it must exist before InitializeComponent evaluates them.
        ViewModel = ServiceLocator.AcquireService<WebViewDocumentSettingsViewModel>();

        InitializeComponent();
    }

    /// <summary>
    /// Builds the sections over the document's view model, selecting the one the given key names. Called
    /// the first time the settings are shown, so a document that never opens them builds no section views.
    /// </summary>
    public void Initialize(WebViewDocumentViewModel viewModel, string selectedSectionKey)
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

    // The sections in rail order. The keys are persisted, so changing one drops the section a returning
    // user had open.
    private List<SettingsSection> BuildSections(WebViewDocumentViewModel viewModel)
    {
        var homeView = new WebViewHomeSectionView
        {
            ViewModel = viewModel
        };

        var bookmarksView = new WebViewBookmarksSectionView
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
                BookmarksSectionKey,
                "bs-bookmark",
                _stringLocalizer.GetString("WebView_Settings_BookmarksHeader"),
                _stringLocalizer.GetString("WebView_Settings_BookmarksDescription"),
                bookmarksView),
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

    private void ReturnToPageButton_Click(object sender, RoutedEventArgs e)
    {
        ReturnToPageRequested?.Invoke(this, EventArgs.Empty);
    }
}
