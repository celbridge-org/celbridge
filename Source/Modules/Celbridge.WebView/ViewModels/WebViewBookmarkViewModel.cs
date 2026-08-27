using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.WebView.Helpers;
using Microsoft.Extensions.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WebView.ViewModels;

/// <summary>
/// One bookmark as its settings card and its toolbar button present it: the page it opens, the name the
/// button carries, and an optional icon named from the bundled icon set.
/// </summary>
public partial class WebViewBookmarkViewModel : ObservableObject
{
    private readonly IIconService _iconService;
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
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    [NotifyPropertyChangedFor(nameof(IsIconUnknown))]
    private string _icon = string.Empty;

    /// <summary>
    /// The text the toolbar button and the collapsed card show. An unnamed bookmark falls back to its URL,
    /// so a card added from the current page is recognizable before it is named.
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

            return _stringLocalizer.GetString("WebView_Settings_BookmarkUntitled");
        }
    }

    public string UrlLabel => _stringLocalizer.GetString("WebView_Settings_BookmarkUrlLabel");
    public string NameLabel => _stringLocalizer.GetString("WebView_Settings_BookmarkNameLabel");
    public string IconLabel => _stringLocalizer.GetString("WebView_Settings_BookmarkIconLabel");
    public string UrlPlaceholder => _stringLocalizer.GetString("WebView_UrlBar_AddressPlaceholder");
    public string NamePlaceholder => _stringLocalizer.GetString("WebView_Settings_BookmarkNamePlaceholder");
    public string IconPlaceholder => _stringLocalizer.GetString("WebView_Settings_BookmarkIconPlaceholder");
    public string IconHint => _stringLocalizer.GetString("WebView_Settings_BookmarkIconHint");
    public string UnknownIconText => _stringLocalizer.GetString("WebView_Settings_BookmarkUnknownIcon");
    public string InvalidUrlText => _stringLocalizer.GetString("WebView_InvalidUrl");
    public string OpenTooltip => _stringLocalizer.GetString("WebView_Settings_BookmarkOpenTooltip");

    /// <summary>
    /// True when the bookmark names an icon, so the button and card show a glyph beside the name.
    /// </summary>
    public bool HasIcon => !string.IsNullOrWhiteSpace(Icon);

    /// <summary>
    /// True when the named icon is not one the bundled set carries, so the card can say so. The button
    /// still draws, the icon service resolving an unknown name to a fallback glyph.
    /// </summary>
    public bool IsIconUnknown
    {
        get
        {
            if (!HasIcon)
            {
                return false;
            }

            return !_iconService.TryGetGlyph(Icon, out _);
        }
    }

    /// <summary>
    /// True when the bookmark holds an address the document can navigate to, which is what the bookmarks
    /// bar requires before it offers a button for it.
    /// </summary>
    public bool IsNavigable => WebViewUrlHelper.TryNormalize(Url, out _);

    /// <summary>
    /// True when the bookmark holds an address that cannot be navigated to. A blank URL is unconfigured
    /// rather than wrong, so it does not report as invalid.
    /// </summary>
    public bool IsUrlInvalid => !string.IsNullOrWhiteSpace(Url) && !IsNavigable;

    public WebViewBookmarkViewModel(IIconService iconService, IStringLocalizer stringLocalizer)
    {
        _iconService = iconService;
        _stringLocalizer = stringLocalizer;
    }

    /// <summary>
    /// The storage record for this bookmark.
    /// </summary>
    public WebViewBookmark ToBookmark()
    {
        return new WebViewBookmark(Url.Trim(), Name.Trim(), Icon.Trim());
    }
}
