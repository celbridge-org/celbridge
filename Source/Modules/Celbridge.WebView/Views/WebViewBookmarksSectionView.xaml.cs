using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.WebHost;
using Celbridge.WebView.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.Views;

/// <summary>
/// The Bookmarks section of the Web View settings: the pages the document's bookmarks bar offers, each
/// edited through its own card.
/// </summary>
public sealed partial class WebViewBookmarksSectionView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private WebViewDocumentViewModel? _viewModel;

    // Supplied by the surface that owns this section. Assigning it refreshes the bindings so the section
    // populates once the surface hands over its instance.
    public WebViewDocumentViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            BookmarkCards.ItemsSource = value?.Bookmarks;
            Bindings?.Update();
        }
    }

    public string AddCurrentPageString => _stringLocalizer.GetString("WebView_Settings_AddBookmarkFromCurrentPage");
    public string AddBookmarkString => _stringLocalizer.GetString("WebView_Settings_AddBookmark");
    public string NoBookmarksString => _stringLocalizer.GetString("WebView_Settings_NoBookmarks");

    public WebViewBookmarksSectionView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        InitializeComponent();

        BookmarkCards.AddRequested += BookmarkCards_AddRequested;
    }

    /// <summary>
    /// Opens the given bookmark's card and brings it into view, for a caller that arrived here to look at
    /// that one bookmark.
    /// </summary>
    public void RevealBookmark(WebViewBookmarkViewModel bookmark)
    {
        BookmarkCards.ExpandCard(bookmark);
    }

    private void BookmarkCards_AddRequested(object? sender, EventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        // A blank bookmark, for the user to fill in. The document records the addition, so there is nothing
        // for the card list to report back.
        var bookmark = ViewModel.CreateBookmark(new WebViewBookmark(string.Empty));
        ViewModel.Bookmarks.Add(bookmark);
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
        RevealBookmark(bookmark);
    }

    private void SetAsHomeButton_Click(object sender, RoutedEventArgs e)
    {
        var setAsHomeButton = (FrameworkElement)sender;
        if (setAsHomeButton.DataContext is not WebViewBookmarkViewModel bookmark)
        {
            return;
        }

        ViewModel?.SetBookmarkAsHome(bookmark);
    }

    private void OpenBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var openButton = (FrameworkElement)sender;
        if (openButton.DataContext is not WebViewBookmarkViewModel bookmark)
        {
            return;
        }

        ViewModel?.OpenBookmark(bookmark);
    }
}
