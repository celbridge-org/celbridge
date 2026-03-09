using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;

namespace Celbridge.Search.ViewModels;

public partial class SearchMatchLineViewModel : ObservableObject
{
    internal readonly SearchFileResultViewModel Parent;

    public int LineNumber { get; }

    /// <summary>
    /// The full context line text (used for tooltip).
    /// </summary>
    public string LineText { get; }

    /// <summary>
    /// Text before the match (for display).
    /// </summary>
    public string TextBeforeMatch { get; }

    /// <summary>
    /// The matched text (for highlighted display).
    /// </summary>
    public string MatchedText { get; }

    /// <summary>
    /// Text after the match (for display).
    /// </summary>
    public string TextAfterMatch { get; }

    /// <summary>
    /// The position where the match starts in the display text.
    /// </summary>
    public int MatchStart { get; }

    /// <summary>
    /// The length of the match.
    /// </summary>
    public int MatchLength { get; }

    /// <summary>
    /// The position where the match starts in the original unformatted line (0-based).
    /// This is used for navigation to the correct column in the editor.
    /// </summary>
    public int OriginalMatchStart { get; }

    public bool IsReplaceModeEnabled => Parent.IsReplaceModeEnabled;

    public string ReplaceMatchTooltip { get; }

    public SearchMatchLineViewModel(SearchMatchLine match, SearchFileResultViewModel parent)
    {
        Parent = parent;
        LineNumber = match.LineNumber;
        LineText = match.LineText;
        MatchStart = match.MatchStart;
        MatchLength = match.MatchLength;
        OriginalMatchStart = match.OriginalMatchStart;

        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ReplaceMatchTooltip = stringLocalizer.GetString("SearchPanel_ReplaceMatchTooltip");

        // Split the line text into before, match, and after segments for highlighting
        var displayText = match.LineText;
        var matchStart = match.MatchStart;
        var matchLength = match.MatchLength;

        // Ensure bounds are valid
        if (matchStart >= 0 && matchStart < displayText.Length)
        {
            var matchEnd = Math.Min(matchStart + matchLength, displayText.Length);

            TextBeforeMatch = displayText.Substring(0, matchStart);
            MatchedText = displayText.Substring(matchStart, matchEnd - matchStart);
            TextAfterMatch = matchEnd < displayText.Length ? displayText.Substring(matchEnd) : string.Empty;
        }
        else
        {
            // Fallback if match position is invalid
            TextBeforeMatch = displayText;
            MatchedText = string.Empty;
            TextAfterMatch = string.Empty;
        }

        // Subscribe to parent's property changes to update IsReplaceModeEnabled binding
        Parent.Parent.PropertyChanged += GrandParent_PropertyChanged;
    }

    private void GrandParent_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchPanelViewModel.IsReplaceModeEnabled))
        {
            OnPropertyChanged(nameof(IsReplaceModeEnabled));
        }
    }

    [RelayCommand]
    private void Navigate()
    {
        // Navigate to the line and column position of the match
        // Use OriginalMatchStart (0-based) + 1 to get the 1-based column position for Monaco
        Parent.Parent.NavigateToResult(Parent.Resource, LineNumber, OriginalMatchStart + 1);
    }

    [RelayCommand]
    private async Task ReplaceMatch()
    {
        await Parent.Parent.ReplaceMatchAsync(this);
    }
}
