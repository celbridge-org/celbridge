using Celbridge.Documents;
using Microsoft.Extensions.Localization;

namespace Celbridge.Search.ViewModels;

/// <summary>
/// Contains replace operations for search results.
/// </summary>
public partial class SearchPanelViewModel
{
    public async Task ReplaceInFileAsync(SearchFileResultViewModel fileResult)
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        IsReplacing = true;

        try
        {
            var textEdits = BuildTextEditsForMatches(fileResult.Matches);

            var documentEdit = new DocumentEdit(fileResult.Resource, textEdits);
            var documentEdits = new List<DocumentEdit> { documentEdit };

            await ApplyEditsAsync(documentEdits);

            _ = SaveReplaceTermToHistoryAsync();

            FileResults.Remove(fileResult);
            UpdateResultsStatus();
        }
        finally
        {
            IsReplacing = false;
        }
    }

    public async Task ReplaceMatchAsync(SearchMatchLineViewModel matchLine)
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        var selectedMatches = GetSelectedMatches();
        if (matchLine.IsSelected && selectedMatches.Count > 1)
        {
            await ReplaceSelectedMatchesAsync(selectedMatches);
            return;
        }

        _ = SaveReplaceTermToHistoryAsync();

        IsReplacing = true;

        try
        {
            var fileResult = matchLine.Parent;

            var textEdit = CreateTextEditForMatch(matchLine);

            var documentEdit = new DocumentEdit(fileResult.Resource, new List<TextEdit> { textEdit });
            var documentEdits = new List<DocumentEdit> { documentEdit };

            await ApplyEditsAsync(documentEdits);

            fileResult.RemoveMatch(matchLine);

            if (fileResult.MatchCount == 0)
            {
                FileResults.Remove(fileResult);
            }

            UpdateResultsStatus();
        }
        finally
        {
            IsReplacing = false;
        }
    }

    private async Task ReplaceSelectedMatchesAsync(List<SearchMatchLineViewModel> selectedMatches)
    {
        if (selectedMatches.Count == 0)
        {
            return;
        }

        IsReplacing = true;

        try
        {
            var matchesByFile = selectedMatches
                .GroupBy(m => m.Parent.Resource)
                .ToList();

            var documentEdits = new List<DocumentEdit>();

            foreach (var fileGroup in matchesByFile)
            {
                var textEdits = BuildTextEditsForMatches(fileGroup);
                var documentEdit = new DocumentEdit(fileGroup.Key, textEdits);
                documentEdits.Add(documentEdit);
            }

            await ApplyEditsAsync(documentEdits);

            foreach (var match in selectedMatches)
            {
                var fileResult = match.Parent;
                fileResult.RemoveMatch(match);

                if (fileResult.MatchCount == 0)
                {
                    FileResults.Remove(fileResult);
                }
            }

            UpdateResultsStatus();
        }
        finally
        {
            IsReplacing = false;
        }
    }

    private async Task ReplaceAllCoreAsync()
    {
        if (string.IsNullOrEmpty(SearchText) || FileResults.Count == 0)
        {
            return;
        }

        IsReplacing = true;

        var progressTitle = _stringLocalizer.GetString("SearchPanel_ReplaceAllProgress");
        var progressToken = _dialogService.AcquireProgressDialog(progressTitle);

        try
        {
            var allResults = await _searchService.SearchAsync(
                SearchText,
                MatchCase,
                WholeWord,
                maxResults: null,
                CancellationToken.None);

            if (allResults.TotalMatches == 0)
            {
                return;
            }

            var totalMatches = allResults.TotalMatches;
            var totalFiles = allResults.TotalFiles;
            var titleText = _stringLocalizer.GetString("SearchPanel_ReplaceAllConfirmTitle");
            var messageText = _stringLocalizer.GetString(
                "SearchPanel_ReplaceAllConfirmMessage",
                totalMatches,
                totalFiles);

            progressToken.Dispose();

            var confirmResult = await _dialogService.ShowConfirmationDialogAsync(titleText, messageText);
            if (!confirmResult.IsSuccess || !confirmResult.Value)
            {
                return;
            }

            progressToken = _dialogService.AcquireProgressDialog(progressTitle);

            var documentEdits = BuildDocumentEditsForResults(allResults);

            await ApplyEditsAsync(documentEdits);

            _ = SaveReplaceTermToHistoryAsync();

            FileResults.Clear();
            UpdateResultsStatus();
        }
        finally
        {
            progressToken.Dispose();
            IsReplacing = false;
        }
    }

    private List<TextEdit> BuildTextEditsForMatches(IEnumerable<SearchMatchLineViewModel> matches)
    {
        return matches
            .OrderByDescending(m => m.LineNumber)
            .ThenByDescending(m => m.OriginalMatchStart)
            .Select(CreateTextEditForMatch)
            .ToList();
    }

    private List<DocumentEdit> BuildDocumentEditsForResults(SearchResults results)
    {
        var documentEdits = new List<DocumentEdit>();

        foreach (var fileResult in results.FileResults)
        {
            var textEdits = fileResult.Matches
                .OrderByDescending(m => m.LineNumber)
                .ThenByDescending(m => m.OriginalMatchStart)
                .Select(m => new TextEdit(
                    Line: m.LineNumber,
                    Column: m.OriginalMatchStart + 1,
                    EndLine: m.LineNumber,
                    EndColumn: m.OriginalMatchStart + 1 + SearchText.Length,
                    NewText: ReplaceText))
                .ToList();

            var documentEdit = new DocumentEdit(fileResult.Resource, textEdits);
            documentEdits.Add(documentEdit);
        }

        return documentEdits;
    }

    private TextEdit CreateTextEditForMatch(SearchMatchLineViewModel match)
    {
        return new TextEdit(
            Line: match.LineNumber,
            Column: match.OriginalMatchStart + 1,
            EndLine: match.LineNumber,
            EndColumn: match.OriginalMatchStart + 1 + SearchText.Length,
            NewText: ReplaceText);
    }

    private async Task ApplyEditsAsync(List<DocumentEdit> documentEdits)
    {
        await _commandService.ExecuteAsync<IApplyEditsCommand>(command =>
        {
            command.Edits = documentEdits;
        });
    }
}
