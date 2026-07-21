using Celbridge.Logging;
using Celbridge.WebHost;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.WebView.Views;

/// <summary>
/// A find bar for whole-page web content. It runs find through an injected IWebViewFindTarget, so it carries
/// no WebView2 specifics of its own.
/// </summary>
public sealed partial class WebViewFindBar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ILogger<WebViewFindBar> _logger;

    // Coalesces a burst of search-box keystrokes into a single find.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _debounceTimer;

    private IWebViewFindTarget? _target;

    // True once a find session has been started for the current term. Opening the bar does not search, so
    // reopening with a preserved term leaves the page scroll untouched until the user explicitly searches.
    private bool _hasActiveFind;

    /// <summary>
    /// Raised when the bar closes, so the host can return focus to its web content.
    /// </summary>
    public event EventHandler? Closed;

    public WebViewFindBar()
    {
        this.InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _logger = ServiceLocator.AcquireService<ILogger<WebViewFindBar>>();

        FindTextBox.PlaceholderText = _stringLocalizer.GetString("WebView_Find_Placeholder");
        ToolTipService.SetToolTip(MatchCaseButton, _stringLocalizer.GetString("WebView_Find_MatchCase").Value);
        ToolTipService.SetToolTip(FindPreviousButton, _stringLocalizer.GetString("WebView_Find_Previous").Value);
        ToolTipService.SetToolTip(FindNextButton, _stringLocalizer.GetString("WebView_Find_Next").Value);
        ToolTipService.SetToolTip(FindCloseButton, _stringLocalizer.GetString("WebView_Find_Close").Value);

        _debounceTimer = DispatcherQueue.CreateTimer();
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(200);
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    /// <summary>
    /// Connects the bar to the web content it drives. Call once before the bar is shown.
    /// </summary>
    public void Attach(IWebViewFindTarget target)
    {
        _target = target;
    }

    /// <summary>
    /// Reveals and focuses the bar without searching: a preserved term is selected so the next keystroke
    /// replaces it, and the page scroll stays put until the user explicitly searches.
    /// </summary>
    public void Begin()
    {
        Visibility = Visibility.Visible;
        FindTextBox.Focus(FocusState.Programmatic);
        FindTextBox.SelectAll();
    }

    /// <summary>
    /// Hides the bar, clears the match selection, and raises Closed.
    /// </summary>
    public void Close()
    {
        _debounceTimer?.Stop();
        _hasActiveFind = false;
        Visibility = Visibility.Collapsed;
        MatchSummaryText.Visibility = Visibility.Collapsed;

        _target?.StopFind();

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // The Skia TextBox inserts a literal tab on Tab (KeyDown.Handled does not gate it), and tabs/newlines
        // can never match anyway. Strip them, then let default Tab navigation move focus. Re-assigning Text
        // re-enters this handler with the cleaned value, which then drives the search.
        var cleaned = FindTextBox.Text
            .Replace("\t", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty);
        if (cleaned != FindTextBox.Text)
        {
            var caret = Math.Min(FindTextBox.SelectionStart, cleaned.Length);
            FindTextBox.Text = cleaned;
            FindTextBox.SelectionStart = caret;
            return;
        }

        if (string.IsNullOrEmpty(FindTextBox.Text))
        {
            _debounceTimer?.Stop();
            _hasActiveFind = false;
            MatchSummaryText.Visibility = Visibility.Collapsed;
            _target?.StopFind();
            return;
        }

        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void DebounceTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        StartFind();
    }

    private void FindTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;

            var shiftDown = InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(CoreVirtualKeyStates.Down);

            StepFind(previous: shiftDown);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void FindNextButton_Click(object sender, RoutedEventArgs e)
    {
        StepFind(previous: false);
    }

    private void FindPreviousButton_Click(object sender, RoutedEventArgs e)
    {
        StepFind(previous: true);
    }

    private void FindCloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MatchCaseButton_Click(object sender, RoutedEventArgs e)
    {
        // Re-run an active search so results reflect the new case sensitivity. If nothing is searched yet, the
        // next search picks up the toggle state.
        if (_hasActiveFind)
        {
            StartFind();
        }
    }

    private void StepFind(bool previous)
    {
        if (_target is null
            || string.IsNullOrEmpty(FindTextBox.Text))
        {
            return;
        }

        // First explicit search since the bar opened: start the session (which finds and scrolls to the first
        // match). Once active, next/previous step within it.
        if (!_hasActiveFind)
        {
            StartFind();
            return;
        }

        if (previous)
        {
            _target.FindPrevious();
        }
        else
        {
            _target.FindNext();
        }
    }

    private async void StartFind()
    {
        if (_target is null)
        {
            return;
        }

        var term = FindTextBox.Text;
        if (string.IsNullOrEmpty(term))
        {
            _hasActiveFind = false;
            return;
        }

        _hasActiveFind = true;

        var caseSensitive = MatchCaseButton.IsChecked == true;
        var options = new FindOptions(CaseSensitive: caseSensitive, OnMatchStateChanged: OnMatchStateChanged);

        try
        {
            await _target.StartFindAsync(term, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start find");
        }
    }

    private void OnMatchStateChanged(FindMatchState state)
    {
        // The reporting hook can fire from a native completion handler, so marshal before touching the bar.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (state.MatchCount.HasValue)
            {
                var matchCount = state.MatchCount.Value;
                if (matchCount == 0)
                {
                    MatchSummaryText.Text = _stringLocalizer.GetString("WebView_Find_NoResults");
                }
                else
                {
                    var activeMatchIndex = state.ActiveMatchIndex ?? 0;
                    MatchSummaryText.Text = _stringLocalizer.GetString("WebView_Find_MatchCount", activeMatchIndex, matchCount);
                }

                MatchSummaryText.Visibility = Visibility.Visible;
                return;
            }

            // No free match total (macOS findString): show a not-found hint, otherwise no counter.
            if (!state.MatchFound
                && !string.IsNullOrEmpty(FindTextBox.Text))
            {
                MatchSummaryText.Text = _stringLocalizer.GetString("WebView_Find_NoResults");
                MatchSummaryText.Visibility = Visibility.Visible;
            }
            else
            {
                MatchSummaryText.Visibility = Visibility.Collapsed;
            }
        });
    }
}
