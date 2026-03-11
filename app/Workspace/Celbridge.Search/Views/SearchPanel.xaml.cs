using Celbridge.Search.ViewModels;
using Celbridge.Workspace;
using Windows.System;
using Microsoft.UI.Input;

namespace Celbridge.Search.Views;

public sealed partial class SearchPanel : UserControl, ISearchPanel
{
    private readonly IPanelFocusService _panelFocusService;

    // Saved scroll position for refresh operations
    private double _savedScrollOffset;

    public SearchPanelViewModel ViewModel { get; }

    public SearchPanel()
    {
        _panelFocusService = ServiceLocator.AcquireService<IPanelFocusService>();
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
        // Remove all items except the Clear History item and separator (first two items)
        while (SearchHistoryFlyout.Items.Count > 2)
        {
            SearchHistoryFlyout.Items.RemoveAt(SearchHistoryFlyout.Items.Count - 1);
        }

        // Add history items
        foreach (var term in ViewModel.SearchHistory)
        {
            var menuItem = new MenuFlyoutItem
            {
                Text = term,
                Tag = term
            };
            menuItem.Click += OnSearchHistoryItemClick;
            SearchHistoryFlyout.Items.Add(menuItem);
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
        // Remove all items except the Clear History item and separator (first two items)
        while (ReplaceHistoryFlyout.Items.Count > 2)
        {
            ReplaceHistoryFlyout.Items.RemoveAt(ReplaceHistoryFlyout.Items.Count - 1);
        }

        // Add history items
        foreach (var term in ViewModel.ReplaceHistory)
        {
            var menuItem = new MenuFlyoutItem
            {
                Text = term,
                Tag = term
            };
            menuItem.Click += OnReplaceHistoryItemClick;
            ReplaceHistoryFlyout.Items.Add(menuItem);
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

    private void UserControl_GotFocus(object sender, RoutedEventArgs e)
    {
        _panelFocusService.SetFocusedPanel(WorkspacePanel.Search);
    }

    private void UserControl_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _panelFocusService.SetFocusedPanel(WorkspacePanel.Search);
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

        // Check for modifier keys
        var isCtrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var isShiftPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

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

        // Check for modifier keys
        var isCtrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var isShiftPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

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
        SearchTextBox.Focus(FocusState.Programmatic);
        SearchTextBox.SelectAll();
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
