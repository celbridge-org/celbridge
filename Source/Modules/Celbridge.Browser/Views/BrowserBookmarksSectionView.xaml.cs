using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.WebHost;
using Celbridge.Browser.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Browser.Views;

/// <summary>
/// The Bookmarks section of the browser settings: the pages the document's bookmark list offers, each edited through
/// a card. A read-only file shows the cards with their editing controls disabled, under a notice.
/// </summary>
public sealed partial class BrowserBookmarksSectionView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private BrowserDocumentViewModel? _viewModel;

    // Supplied by the surface that owns this section. Assigning it refreshes the bindings so the section
    // populates once the surface hands over its instance.
    public BrowserDocumentViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            BookmarkCards.ItemsSource = value?.Bookmarks;
            Bindings?.Update();
        }
    }

    public string AddCurrentPageString => _stringLocalizer.GetString("Browser_Settings_AddBookmarkFromCurrentPage");
    public string AddBookmarkString => _stringLocalizer.GetString("Browser_Settings_AddBookmark");
    public string NoBookmarksString => _stringLocalizer.GetString("Browser_Settings_NoBookmarks");
    public string ReadOnlyString => _stringLocalizer.GetString("Browser_Settings_BookmarksReadOnly");

    public BrowserBookmarksSectionView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        InitializeComponent();

        BookmarkCards.AddRequested += BookmarkCards_AddRequested;
    }

    private void BookmarkCards_AddRequested(object? sender, EventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        // A blank bookmark, for the user to fill in. The document records the addition, so there is nothing
        // for the card list to report back.
        ViewModel.AddBookmark(new BrowserBookmark(string.Empty));
    }

    private void CommitField_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        FocusNavigationHelper.CommitFieldOnEnter(sender, e);
    }

    private void AddCurrentPageButton_Click(object sender, RoutedEventArgs e)
    {
        var bookmark = ViewModel?.AddBookmarkFromCurrentPage();
        if (bookmark is null)
        {
            return;
        }

        // The card list only opens what its own add button asked for, so an entry added from here says so.
        BookmarkCards.ExpandCard(bookmark);
    }

    private void OpenBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var openButton = (FrameworkElement)sender;
        if (openButton.DataContext is not BrowserBookmarkViewModel bookmark)
        {
            return;
        }

        ViewModel?.OpenBookmark(bookmark);
    }
}
