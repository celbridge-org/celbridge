using Celbridge.Search.ViewModels;
using Celbridge.UserInterface.Helpers;
using Windows.System;

namespace Celbridge.Search.Views;

public sealed partial class SearchPanel : UserControl, ISearchPanel
{
    // Saved scroll position for refresh operations
    private double _savedScrollOffset;

    public SearchPanelViewModel ViewModel { get; }

    public SearchPanel()
    {
        ViewModel = ServiceLocator.AcquireService<SearchPanelViewModel>();

        this.InitializeComponent();

        // Subscribe to refresh events for scroll position preservation
        ViewModel.BeforeResultsRefresh += OnBeforeResultsRefresh;
        ViewModel.AfterResultsRefresh += OnAfterResultsRefresh;

        // Subscribe to flyout opening events to populate history items
        SearchHistoryFlyout.Opening += OnSearchHistoryFlyoutOpening;
        ReplaceHistoryFlyout.Opening += OnReplaceHistoryFlyoutOpening;
    }

    private void OnSearchHistoryFlyoutOpening(object? sender, object e)
    {
        // Remove all dynamic history items (keep the static separator + clear item at the end)
        while (SearchHistoryFlyout.Items.Count > 2)
        {
            SearchHistoryFlyout.Items.RemoveAt(0);
        }

        // Insert history items before the separator and clear item
        var insertIndex = 0;
        foreach (var term in ViewModel.SearchHistory)
        {
            var menuItem = new MenuFlyoutItem
            {
                Text = term,
                Tag = term
            };
            menuItem.Click += OnSearchHistoryItemClick;
            SearchHistoryFlyout.Items.Insert(insertIndex, menuItem);
            insertIndex++;
        }
    }

    private void OnSearchHistoryItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string term)
        {
            ViewModel.SelectSearchHistoryEntry(term);
        }
    }

    private void OnReplaceHistoryFlyoutOpening(object? sender, object e)
    {
        // Remove all dynamic history items (keep the static separator + clear item at the end)
        while (ReplaceHistoryFlyout.Items.Count > 2)
        {
            ReplaceHistoryFlyout.Items.RemoveAt(0);
        }

        // Insert history items before the separator and clear item
        var insertIndex = 0;
        foreach (var term in ViewModel.ReplaceHistory)
        {
            var menuItem = new MenuFlyoutItem
            {
                Text = term,
                Tag = term
            };
            menuItem.Click += OnReplaceHistoryItemClick;
            ReplaceHistoryFlyout.Items.Insert(insertIndex, menuItem);
            insertIndex++;
        }
    }

    private void OnReplaceHistoryItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string term)
        {
            ViewModel.SelectReplaceHistoryEntry(term);
        }
    }

    private void OnBeforeResultsRefresh(object? sender, EventArgs e)
    {
        // Save the current scroll position before results are refreshed
        _savedScrollOffset = ResultsScrollViewer.VerticalOffset;
    }

    private void OnAfterResultsRefresh(object? sender, EventArgs e)
    {
        // Restore scroll position after results are refreshed
        // Use DispatcherQueue to ensure the UI has updated before scrolling
        DispatcherQueue.TryEnqueue(() =>
        {
            ResultsScrollViewer.ChangeView(null, _savedScrollOffset, null, disableAnimation: true);
        });
    }

    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Save search term to history when focus leaves the search box
        ViewModel.SaveSearchTermToHistory();
    }

    private void ReplaceTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Save replace term to history when focus leaves the replace box
        ViewModel.SaveReplaceTermToHistory();
    }

    private void FileHeader_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not SearchFileResultViewModel fileResult)
        {
            return;
        }

        // Check for modifier keys. The command modifier is Cmd on macOS, Control elsewhere.
        var isCtrlPressed = EditKeyboard.IsCommandModifierDown();
        var isShiftPressed = EditKeyboard.IsShiftDown();

        // Handle selection
        ViewModel.SelectFileResult(fileResult, isCtrlPressed, isShiftPressed);
    }

    private void FileHeader_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is SearchFileResultViewModel fileResult)
        {
            fileResult.IsPointerOver = true;
        }
    }

    private void FileHeader_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is SearchFileResultViewModel fileResult)
        {
            fileResult.IsPointerOver = false;
        }
    }

    private void MatchLine_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not SearchMatchLineViewModel matchLine)
        {
            return;
        }

        // Check for modifier keys. The command modifier is Cmd on macOS, Control elsewhere.
        var isCtrlPressed = EditKeyboard.IsCommandModifierDown();
        var isShiftPressed = EditKeyboard.IsShiftDown();

        // Handle selection
        ViewModel.SelectMatchLine(matchLine, isCtrlPressed, isShiftPressed);

        // Navigate to the result (unless using modifier keys for multi-select)
        if (!isCtrlPressed && !isShiftPressed)
        {
            matchLine.NavigateCommand.Execute(null);
        }
    }

    private void MatchLine_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is SearchMatchLineViewModel matchLine)
        {
            matchLine.IsPointerOver = true;
        }
    }

    private void MatchLine_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is SearchMatchLineViewModel matchLine)
        {
            matchLine.IsPointerOver = false;
        }
    }

    public void FocusSearchInput()
    {
        // Pointer focus state so the central PanelFocusTracker reports the panel (it ignores Programmatic
        // focus). Used when a deliberate gesture (activity-rail selection, panel title-bar click) should
        // move keyboard focus into the search box.
        SearchTextBox.Focus(FocusState.Pointer);
        SearchTextBox.SelectAll();
    }

    private void PanelHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        FocusSearchInput();
    }

    public void SetSearchText(string searchText)
    {
        ViewModel.SearchText = searchText;
    }

    public void SetMatchCase(bool matchCase)
    {
        ViewModel.MatchCase = matchCase;
    }

    public void SetWholeWord(bool wholeWord)
    {
        ViewModel.WholeWord = wholeWord;
    }

    public bool IsReplaceModeEnabled => ViewModel.IsReplaceModeEnabled;

    public void SetReplaceMode(bool enabled)
    {
        ViewModel.IsReplaceModeEnabled = enabled;
    }

    public void SetReplaceText(string replaceText)
    {
        ViewModel.ReplaceText = replaceText;
    }

    public void ExecuteSearch()
    {
        ViewModel.ExecuteSearchCommand.Execute(null);
    }

    public async Task ExecuteReplaceAllAsync()
    {
        await ViewModel.ReplaceAllCommand.ExecuteAsync(null);
    }

    public async Task ExecuteReplaceSelectedAsync()
    {
        var selectedMatches = ViewModel.GetSelectedMatches();
        if (selectedMatches.Count > 0)
        {
            await ViewModel.ReplaceMatchAsync(selectedMatches[0]);
        }
    }
}
