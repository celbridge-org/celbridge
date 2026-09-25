using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Animation;

// The Uno SDK's implicit global usings include System.Windows.Input, which on the Windows head also
// contains a FocusManager type, so the bare name is ambiguous there.
using FocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// One row in the list a title bar badge opens. Each row is identified by the entry it shows, so the
/// keyboard can return to the same row after the list is rebuilt.
/// </summary>
internal interface IBadgeRow
{
    /// <summary>
    /// The id of the entry the row shows.
    /// </summary>
    long EntryId { get; }

    /// <summary>
    /// Moves keyboard focus to what the row offers first. False when it has nothing that can take it.
    /// </summary>
    bool TryFocusButton();
}

/// <summary>
/// Where the keyboard was in a badge's list before its rows were rebuilt: the entry its row showed, if it
/// was on a row, and that row's position.
/// </summary>
internal sealed record BadgeListFocus(long? EntryId, int RowIndex);

/// <summary>
/// The list a title bar badge opens: how tall it grows, where the keyboard goes when its rows are
/// rebuilt, and the pulse that marks an arrival. The badges differ in what they list, not in how the
/// list behaves, so they share this.
/// </summary>
internal sealed class BadgeList
{
    // The tallest the list grows before it scrolls, and how far it stops short of the bottom of a window
    // too short to hold that much.
    private const double ListHeightLimit = 560;
    private const double ListWindowClearance = 120;

    private readonly UserControl _badge;
    private readonly ScrollViewer _scrollViewer;
    private readonly Panel _rows;
    private readonly Button _clearAllButton;
    private readonly UIElement _flashOverlay;

    private bool _isFlashRequested;
    private long _flashEndTick;
    private Storyboard? _flashStoryboard;

    public BadgeList(
        UserControl badge,
        ScrollViewer scrollViewer,
        Panel rows,
        Button clearAllButton,
        UIElement flashOverlay)
    {
        _badge = badge;
        _scrollViewer = scrollViewer;
        _rows = rows;
        _clearAllButton = clearAllButton;
        _flashOverlay = flashOverlay;
    }

    /// <summary>
    /// Whether the list is open. Only an open list is worth handing the keyboard back to.
    /// </summary>
    public bool IsOpen { get; set; }

    /// <summary>
    /// Caps the list to the window it opens over, so a full one scrolls rather than running off the bottom.
    /// </summary>
    public void CapHeightToWindow()
    {
        var windowHeight = _badge.XamlRoot?.Size.Height ?? 0;
        if (windowHeight <= 0)
        {
            return;
        }

        var listHeight = Math.Min(ListHeightLimit, windowHeight - ListWindowClearance);

        _scrollViewer.MaxHeight = Math.Max(listHeight, 0);
    }

    /// <summary>
    /// Pulses the badge to draw the eye to what has just arrived. A run of arrivals shares one pulse.
    /// </summary>
    public void Flash()
    {
        if (_isFlashRequested ||
            Environment.TickCount64 < _flashEndTick)
        {
            return;
        }

        _isFlashRequested = true;

        // Deferred past the next layout pass, so a badge that has only just appeared is on screen when it
        // pulses.
        _badge.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _isFlashRequested = false;

            if (_badge.Visibility != Visibility.Visible)
            {
                return;
            }

            _flashStoryboard = AttentionFlash.Play(_flashOverlay);
            _flashEndTick = Environment.TickCount64 + (long)AttentionFlash.Duration.TotalMilliseconds;
        });
    }

    /// <summary>
    /// Ends a pulse still running, for a badge leaving the window.
    /// </summary>
    public void StopFlash()
    {
        _flashStoryboard?.Stop();
        _flashStoryboard = null;
    }

    /// <summary>
    /// Where the keyboard is in the list, or null when it is elsewhere. Call it before rebuilding the
    /// rows, which destroys the focused element. Clear All counts as the first row.
    /// </summary>
    public BadgeListFocus? FindFocus()
    {
        var xamlRoot = _badge.XamlRoot;
        if (xamlRoot is null)
        {
            return null;
        }

        var element = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
        while (element is not null)
        {
            if (ReferenceEquals(element, _clearAllButton))
            {
                return new BadgeListFocus(EntryId: null, RowIndex: 0);
            }

            if (element is IBadgeRow row)
            {
                var rowIndex = _rows.Children.IndexOf((UIElement)row);

                return new BadgeListFocus(row.EntryId, rowIndex);
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    /// <summary>
    /// Notes where the keyboard is and parks it on the list itself, for a caller about to rebuild the rows.
    /// Null when the keyboard was elsewhere.
    /// </summary>
    public BadgeListFocus? HoldFocusForRebuild()
    {
        var listFocus = FindFocus();
        if (listFocus is null)
        {
            return null;
        }

        // A rebuild takes the focused row out of the tree, and a focus reconcile landing in that gap reads
        // the keyboard as stranded and moves it to the window's placeholder, which dismisses the flyout.
        // Parking it on the list keeps it inside the flyout until the rows are back. The tab stop lasts only
        // for the move: a permanent one would add a step to every Tab through the list.
        _scrollViewer.IsTabStop = true;
        _scrollViewer.Focus(FocusState.Programmatic);
        _scrollViewer.IsTabStop = false;

        return listFocus;
    }

    /// <summary>
    /// Hands the keyboard back to where it was, or as near to it as the rebuilt list allows.
    /// </summary>
    public void RestoreFocus(BadgeListFocus listFocus)
    {
        if (!IsOpen)
        {
            return;
        }

        var rows = _rows.Children
            .OfType<IBadgeRow>()
            .ToList();

        // The row still listing the same entry keeps the keyboard, wherever an arrival has moved it.
        var sameRow = rows.FirstOrDefault(row => row.EntryId == listFocus.EntryId);
        if (sameRow is not null &&
            sameRow.TryFocusButton())
        {
            return;
        }

        var startIndex = Math.Clamp(listFocus.RowIndex, 0, Math.Max(rows.Count - 1, 0));

        // Forward from the row that took the removed one's place, then back towards the top.
        for (var index = startIndex; index < rows.Count; index++)
        {
            if (rows[index].TryFocusButton())
            {
                return;
            }
        }

        for (var index = startIndex - 1; index >= 0; index--)
        {
            if (rows[index].TryFocusButton())
            {
                return;
            }
        }

        // A hidden or disabled Clear All refuses focus, which leaves the keyboard where it is.
        _clearAllButton.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Hands the keyboard back once the new rows have been laid out, which happens after the caller
    /// returns.
    /// </summary>
    public void RestoreFocusWhenLaidOut(BadgeListFocus listFocus)
    {
        _badge.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => RestoreFocus(listFocus));
    }
}
