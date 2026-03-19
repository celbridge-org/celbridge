namespace Celbridge.Search.ViewModels;

/// <summary>
/// Contains selection management for search results (Ctrl+click, Shift+click, range selection).
/// </summary>
public partial class SearchPanelViewModel
{
    /// <summary>
    /// Handles selection of a match line item with support for Ctrl and Shift modifiers.
    /// </summary>
    public void SelectMatchLine(SearchMatchLineViewModel matchLine, bool isCtrlPressed, bool isShiftPressed)
    {
        if (isCtrlPressed)
        {
            matchLine.IsSelected = !matchLine.IsSelected;
            if (matchLine.IsSelected)
            {
                _selectionAnchor = matchLine;
            }
        }
        else if (isShiftPressed && _selectionAnchor != null)
        {
            SelectRange(_selectionAnchor, matchLine);
        }
        else
        {
            ClearAllSelections();
            matchLine.IsSelected = true;
            _selectionAnchor = matchLine;
        }
    }

    /// <summary>
    /// Handles selection of a file result item with support for Ctrl and Shift modifiers.
    /// </summary>
    public void SelectFileResult(SearchFileResultViewModel fileResult, bool isCtrlPressed, bool isShiftPressed)
    {
        if (isCtrlPressed)
        {
            fileResult.IsSelected = !fileResult.IsSelected;
            if (fileResult.IsSelected)
            {
                _selectionAnchor = fileResult;
            }
        }
        else if (isShiftPressed && _selectionAnchor != null)
        {
            SelectRange(_selectionAnchor, fileResult);
        }
        else
        {
            ClearAllSelections();
            fileResult.IsSelected = true;
            _selectionAnchor = fileResult;
        }
    }

    private void ClearAllSelections()
    {
        foreach (var fileResult in FileResults)
        {
            fileResult.IsSelected = false;
            foreach (var match in fileResult.Matches)
            {
                match.IsSelected = false;
            }
        }
    }

    private void SelectRange(ISelectableSearchItem from, ISelectableSearchItem to)
    {
        var allItems = new List<ISelectableSearchItem>();
        foreach (var fileResult in FileResults)
        {
            allItems.Add(fileResult);
            foreach (var match in fileResult.Matches)
            {
                allItems.Add(match);
            }
        }

        var fromIndex = allItems.IndexOf(from);
        var toIndex = allItems.IndexOf(to);

        if (fromIndex == -1 || toIndex == -1)
        {
            return;
        }

        if (fromIndex > toIndex)
        {
            (fromIndex, toIndex) = (toIndex, fromIndex);
        }

        ClearAllSelections();
        for (var i = fromIndex; i <= toIndex; i++)
        {
            allItems[i].IsSelected = true;
        }
    }

    /// <summary>
    /// Gets all currently selected match lines.
    /// </summary>
    public List<SearchMatchLineViewModel> GetSelectedMatches()
    {
        return FileResults
            .SelectMany(f => f.Matches)
            .Where(m => m.IsSelected)
            .ToList();
    }
}
