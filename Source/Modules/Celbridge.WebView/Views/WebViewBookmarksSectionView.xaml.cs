using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.WebView.ViewModels;
using Microsoft.Extensions.Localization;
using Windows.System;

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

    // The fields commit when they lose focus, which leaves Enter looking as though it did nothing. There is
    // no way to push a compiled binding, so Enter writes the field's value to the property its tag names.
    private void BookmarkField_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        var textBox = (TextBox)sender;
        if (textBox.DataContext is not WebViewBookmarkViewModel bookmark)
        {
            return;
        }

        switch (textBox.Tag as string)
        {
            case nameof(WebViewBookmarkViewModel.Url):
                bookmark.Url = textBox.Text;
                break;

            case nameof(WebViewBookmarkViewModel.Name):
                bookmark.Name = textBox.Text;
                break;

            case nameof(WebViewBookmarkViewModel.Icon):
                bookmark.Icon = textBox.Text;
                break;
        }

        e.Handled = true;
    }

    private void AddCurrentPageButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.AddBookmarkFromCurrentPage();
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
