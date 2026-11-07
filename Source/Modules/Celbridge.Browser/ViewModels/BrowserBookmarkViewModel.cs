using Celbridge.WebHost;
using Celbridge.Browser.Helpers;
using Microsoft.Extensions.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Browser.ViewModels;

/// <summary>
/// One bookmark of a browser document. Url is the page to open, and Name is the label. OpenOnStart opens the page in a
/// tab when the document opens with no saved tabs.
/// </summary>
public partial class BrowserBookmarkViewModel : ObservableObject
{
    private readonly IStringLocalizer _stringLocalizer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(IsNavigable))]
    [NotifyPropertyChangedFor(nameof(IsUrlInvalid))]
    private string _url = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _openOnStart;

    // The site's icon as encoded image bytes, or null until the cache has one. The bookmark shows a default icon until
    // then.
    [ObservableProperty]
    private byte[]? _favicon;

    /// <summary>
    /// The text the bookmark list and the collapsed card show. An unnamed bookmark falls back to the URL, which
    /// identifies a card added from the current page until the user names the card.
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
            {
                return Name;
            }

            if (!string.IsNullOrWhiteSpace(Url))
            {
                return Url;
            }

            return _stringLocalizer.GetString("Browser_Settings_BookmarkUntitled");
        }
    }

    public string UrlLabel => _stringLocalizer.GetString("Browser_Settings_BookmarkUrlLabel");
    public string NameLabel => _stringLocalizer.GetString("Browser_Settings_BookmarkNameLabel");
    public string UrlPlaceholder => _stringLocalizer.GetString("Browser_UrlBar_AddressPlaceholder");
    public string NamePlaceholder => _stringLocalizer.GetString("Browser_Settings_BookmarkNamePlaceholder");
    public string InvalidUrlText => _stringLocalizer.GetString("Browser_InvalidUrl");
    public string OpenTooltip => _stringLocalizer.GetString("Browser_Settings_BookmarkOpenTooltip");
    public string OpenOnStartLabel => _stringLocalizer.GetString("Browser_Settings_BookmarkOpenOnStartLabel");
    public string OpenOnStartHint => _stringLocalizer.GetString("Browser_Settings_BookmarkOpenOnStartHint");

    /// <summary>
    /// True when the bookmark names an address the document can navigate to. The bookmark list offers an item only for
    /// such a bookmark.
    /// </summary>
    public bool IsNavigable => BrowserUrlHelper.TryNormalize(Url, out _);

    /// <summary>
    /// True when the bookmark names an address that cannot be navigated to. A blank URL is unconfigured, so it does
    /// not report as invalid.
    /// </summary>
    public bool IsUrlInvalid => !string.IsNullOrWhiteSpace(Url) && !IsNavigable;

    public BrowserBookmarkViewModel(IStringLocalizer stringLocalizer)
    {
        _stringLocalizer = stringLocalizer;
    }

    /// <summary>
    /// Returns the storage record for this bookmark.
    /// </summary>
    public BrowserBookmark ToBookmark()
    {
        return new BrowserBookmark(Url.Trim(), Name.Trim(), OpenOnStart);
    }
}
