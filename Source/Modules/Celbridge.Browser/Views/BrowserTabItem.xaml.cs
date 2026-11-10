using Celbridge.Browser.ViewModels;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Views.Controls;
using Microsoft.Extensions.Localization;

namespace Celbridge.Browser.Views;

/// <summary>
/// One browser tab in the tab bar: the page's favicon, the tab's label, the close button from the TabViewItem template,
/// and the tab's context menu. The tab has the shape of a document tab. The page shows outside the tab bar.
/// </summary>
public sealed partial class BrowserTabItem : TabViewItem
{
    private const string CloseButtonAutomationId = "BrowserTabCloseButton";
    private const string BookmarkPageStringKey = "Browser_UrlBar_BookmarkPageTooltip";
    private const string RemoveBookmarkStringKey = "Browser_UrlBar_RemoveBookmarkTooltip";

    private readonly IStringLocalizer _stringLocalizer;
    private readonly IBrowserTabMenuOwner _menuOwner;

    public BrowserTabViewModel Tab { get; }

    public BrowserTabItem(BrowserTabViewModel tab, IBrowserTabMenuOwner menuOwner)
    {
        // The tab backs x:Bind paths, so it must be set before InitializeComponent evaluates them.
        Tab = tab;
        _menuOwner = menuOwner;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        InitializeComponent();

        // The tab takes the strip band less the gap it keeps above itself.
        Height = TabStripView.TabHeight;
        Margin = new Thickness(0, BrowserTabBar.TabTopGap, 0, 0);

        InitializeMenu();
    }

    private void InitializeMenu()
    {
        InitializeMenuItem(CloseMenuItem, BrowserTabMenuAction.Close, "DocumentTab_Close");
        InitializeMenuItem(CloseOthersMenuItem, BrowserTabMenuAction.CloseOthers, "DocumentTab_CloseOthers");
        InitializeMenuItem(CloseToTheRightMenuItem, BrowserTabMenuAction.CloseToTheRight, "DocumentTab_CloseRight");
        InitializeMenuItem(CloseToTheLeftMenuItem, BrowserTabMenuAction.CloseToTheLeft, "DocumentTab_CloseLeft");
        InitializeMenuItem(CloseAllMenuItem, BrowserTabMenuAction.CloseAll, "DocumentTab_CloseAll");
        InitializeMenuItem(DuplicateMenuItem, BrowserTabMenuAction.Duplicate, "Browser_TabMenu_Duplicate");
        InitializeMenuItem(BookmarkMenuItem, BrowserTabMenuAction.ToggleBookmark, BookmarkPageStringKey);
        InitializeMenuItem(CopyAddressMenuItem, BrowserTabMenuAction.CopyAddress, "Browser_TabMenu_CopyAddress");
        InitializeMenuItem(
            OpenInSystemBrowserMenuItem,
            BrowserTabMenuAction.OpenInSystemBrowser,
            "Browser_UrlBar_OpenInBrowserTooltip");

        // The menu opens over the page.
        var overlayFlyoutSupport = ServiceLocator.AcquireService<IOverlayFlyoutSupport>();
        overlayFlyoutSupport.Apply(TabMenu, _menuOwner.ReturnKeyboardAfterTabMenu);
    }

    private void InitializeMenuItem(MenuFlyoutItem item, BrowserTabMenuAction action, string stringKey)
    {
        item.Text = _stringLocalizer.GetString(stringKey);
        item.Tag = action;
    }

    // A close item that does not apply hides, as on a document tab. Any other item that does not apply grays out.
    private void TabMenu_Opening(object? sender, object e)
    {
        var state = _menuOwner.GetTabMenuState(Tab);

        var closeItems = state.CloseItems;
        CloseOthersMenuItem.Visibility = closeItems.ShowCloseOthers ? Visibility.Visible : Visibility.Collapsed;
        CloseToTheRightMenuItem.Visibility = closeItems.ShowCloseToTheRight ? Visibility.Visible : Visibility.Collapsed;
        CloseToTheLeftMenuItem.Visibility = closeItems.ShowCloseToTheLeft ? Visibility.Visible : Visibility.Collapsed;
        CloseAllMenuItem.Visibility = closeItems.ShowCloseAll ? Visibility.Visible : Visibility.Collapsed;

        DuplicateMenuItem.IsEnabled = state.CanDuplicate;

        var bookmarkKey = state.IsBookmarked ? RemoveBookmarkStringKey : BookmarkPageStringKey;
        BookmarkMenuItem.Text = _stringLocalizer.GetString(bookmarkKey);
        BookmarkMenuItem.IsEnabled = state.CanToggleBookmark;

        CopyAddressMenuItem.IsEnabled = state.HasAddress;
        OpenInSystemBrowserMenuItem.IsEnabled = state.HasAddress;
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: BrowserTabMenuAction action })
        {
            _menuOwner.RunTabMenuAction(Tab, action);
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("CloseButton") is not Button closeButton)
        {
            return;
        }

        string closeTooltip = _stringLocalizer.GetString("Browser_TabBar_CloseTabTooltip");

        ToolTipService.SetToolTip(closeButton, closeTooltip);
        AutomationProperties.SetName(closeButton, closeTooltip);
        AutomationProperties.SetAutomationId(closeButton, CloseButtonAutomationId);
    }
}
