using System.Collections.Specialized;
using System.ComponentModel;
using Celbridge.Browser.ViewModels;
using Celbridge.UserInterface.DragDrop;
using Celbridge.UserInterface.Views.Controls;
using Microsoft.Extensions.Localization;
using Windows.Foundation;

namespace Celbridge.Browser.Views;

/// <summary>
/// The row at the top of a browser document. The row has the shared tab strip, with one tab per open page, and the
/// button that opens a blank tab. The strip follows the view model's tabs and shown tab. A tab drag moves a tab within
/// this strip.
/// </summary>
public sealed partial class BrowserTabBar : UserControl, ITabDragOwner
{
    private readonly IStringLocalizer _stringLocalizer;

    private BrowserDocumentViewModel? _viewModel;
    private Canvas? _dragOverlay;
    private TabDragGesture? _dragGesture;
    private TabInsertionMarker? _insertionMarker;

    // Set while the strip follows a change in the view model. Removing or moving a tab changes the strip's selection
    // on the way, and that change must not choose a tab.
    private bool _isFollowingViewModel;

    /// <summary>
    /// Raised when the user closes a tab with its close button.
    /// </summary>
    public event EventHandler<BrowserTabViewModel>? TabCloseRequested;

    public BrowserTabBar()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        InitializeComponent();

        string newTabTooltip = _stringLocalizer.GetString("Browser_TabBar_NewTabTooltip");
        ToolTipService.SetToolTip(NewTabButton, newTabTooltip);
        AutomationProperties.SetName(NewTabButton, newTabTooltip);

        // Centered on the tabs, which sit below the gap at the top of the band.
        NewTabButton.Margin = new Thickness(0, TabStripView.TabTopGap, 0, 0);

        TabStrip.TabView.TabCloseRequested += TabView_TabCloseRequested;
        TabStrip.TabView.SelectionChanged += TabView_SelectionChanged;
        TabStrip.TabPressed += TabStrip_TabPressed;
    }

    /// <summary>
    /// The document's view model. Set once, when the document view is constructed.
    /// </summary>
    public BrowserDocumentViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null)
            {
                _viewModel.Tabs.CollectionChanged -= Tabs_CollectionChanged;
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            _viewModel = value;

            if (_viewModel is not null)
            {
                _viewModel.Tabs.CollectionChanged += Tabs_CollectionChanged;
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            FollowViewModel(RebuildTabs);
        }
    }

    /// <summary>
    /// Sets the overlay a tab drag draws its ghost and insertion marker in. The overlay spans the document, so the
    /// ghost follows the pointer over the page.
    /// </summary>
    public void SetDragOverlay(Canvas overlay)
    {
        _dragOverlay = overlay;
        _insertionMarker = new TabInsertionMarker(overlay);
        _dragGesture = new TabDragGesture(this, overlay);
    }

    /// <summary>
    /// Scrolls the shown tab into view once the tab strip has been laid out.
    /// </summary>
    public void BringShownTabIntoView()
    {
        TabStrip.RevealSelectedTab();
    }

    public string GetDragLabel(TabViewItem tab)
    {
        return tab is BrowserTabItem browserTab ? browserTab.Tab.Label : string.Empty;
    }

    // A browser tab stays within its document, so every point drops into this strip, at the slot the pointer's X marks.
    public TabDropSlot? FindDropSlot(TabViewItem tab, Point overlayPosition)
    {
        if (_dragOverlay is null)
        {
            return null;
        }

        var stripBounds = TabStrip.GetStripBounds(_dragOverlay);
        if (stripBounds.IsEmpty)
        {
            return null;
        }

        int slot = TabStrip.GetInsertionSlot(overlayPosition.X, _dragOverlay);

        return new TabDropSlot(TabStrip, slot, OverStrip: stripBounds.Contains(overlayPosition));
    }

    public void ShowDropSlot(TabViewItem tab, TabDropSlot? slot)
    {
        if (slot is null
            || _dragOverlay is null
            || _insertionMarker is null)
        {
            _insertionMarker?.Hide();
            return;
        }

        var stripBounds = TabStrip.GetStripBounds(_dragOverlay);
        var tabBounds = TabStrip.GetTabBounds(_dragOverlay);
        int draggedIndex = TabStrip.TabView.TabItems.IndexOf(tab);

        _insertionMarker.Show(stripBounds, tabBounds, slot.InsertionSlot, draggedIndex);
    }

    public void CompleteDrag(TabViewItem tab, TabDropSlot slot)
    {
        if (ViewModel is null
            || tab is not BrowserTabItem browserTab)
        {
            return;
        }

        int currentIndex = ViewModel.Tabs.IndexOf(browserTab.Tab);
        if (currentIndex >= 0)
        {
            ViewModel.MoveTab(browserTab.Tab, TabDragGesture.GetTargetIndex(currentIndex, slot.InsertionSlot));
        }
    }

    private void TabStrip_TabPressed(TabViewItem tab, PointerRoutedEventArgs e)
    {
        _dragGesture?.OnTabPressed(tab, e);
    }

    private void TabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab is BrowserTabItem browserTab)
        {
            TabCloseRequested?.Invoke(this, browserTab.Tab);
        }
    }

    // A tab the user selects becomes the shown tab.
    private void TabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isFollowingViewModel
            || ViewModel is null
            || TabStrip.TabView.SelectedItem is not BrowserTabItem browserTab
            || ReferenceEquals(browserTab.Tab, ViewModel.ShownTab))
        {
            return;
        }

        ViewModel.ChooseTab(browserTab.Tab);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserDocumentViewModel.ShownTab))
        {
            FollowViewModel(SelectShownTab);
        }
    }

    private void Tabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        FollowViewModel(() => ApplyTabsChange(e));
    }

    private void FollowViewModel(Action change)
    {
        _isFollowingViewModel = true;
        try
        {
            change();
        }
        finally
        {
            _isFollowingViewModel = false;
        }
    }

    private void ApplyTabsChange(NotifyCollectionChangedEventArgs e)
    {
        var tabItems = TabStrip.TabView.TabItems;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                int insertIndex = e.NewStartingIndex;
                foreach (BrowserTabViewModel tab in e.NewItems)
                {
                    var item = new BrowserTabItem(tab);
                    if (insertIndex < 0
                        || insertIndex >= tabItems.Count)
                    {
                        tabItems.Add(item);
                    }
                    else
                    {
                        tabItems.Insert(insertIndex, item);
                    }

                    insertIndex = insertIndex < 0 ? insertIndex : insertIndex + 1;
                }
                break;

            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                foreach (BrowserTabViewModel tab in e.OldItems)
                {
                    if (FindTabItem(tab) is { } item)
                    {
                        TabStrip.RemoveTab(item);
                    }
                }
                break;

            case NotifyCollectionChangedAction.Move when e.NewItems is not null:
                foreach (BrowserTabViewModel tab in e.NewItems)
                {
                    if (FindTabItem(tab) is { } item)
                    {
                        TabStrip.MoveTab(item, e.NewStartingIndex);
                    }
                }
                break;

            default:
                RebuildTabs();
                return;
        }

        SelectShownTab();
    }

    private void RebuildTabs()
    {
        var tabItems = TabStrip.TabView.TabItems;
        foreach (var item in tabItems.ToList())
        {
            TabStrip.RemoveTab(item);
        }

        if (ViewModel is null)
        {
            return;
        }

        foreach (var tab in ViewModel.Tabs)
        {
            tabItems.Add(new BrowserTabItem(tab));
        }

        SelectShownTab();
    }

    private void SelectShownTab()
    {
        var shownTab = ViewModel?.ShownTab;
        var item = shownTab is null ? null : FindTabItem(shownTab);

        if (!ReferenceEquals(TabStrip.TabView.SelectedItem, item))
        {
            TabStrip.SelectTab(item);
        }
    }

    private BrowserTabItem? FindTabItem(BrowserTabViewModel tab)
    {
        return TabStrip.TabView.TabItems
            .OfType<BrowserTabItem>()
            .FirstOrDefault(item => ReferenceEquals(item.Tab, tab));
    }

    // Limits the tabs to the width left beside the new tab button. The button sits right after the last tab, and stays
    // in the row with many tabs open.
    private void BarRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var gapsWidth = (BarRoot.ColumnDefinitions.Count - 1) * BarRoot.ColumnSpacing;

        TabStrip.MaxWidth = Math.Max(0, e.NewSize.Width - NewTabButton.ActualWidth - gapsWidth);
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.OpenBlankTab();
    }
}
