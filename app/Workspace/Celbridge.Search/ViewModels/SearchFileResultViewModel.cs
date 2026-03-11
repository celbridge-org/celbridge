using System.Collections.ObjectModel;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.Search.ViewModels;

public partial class SearchFileResultViewModel : ObservableObject
{
    internal readonly SearchPanelViewModel Parent;

    public ResourceKey Resource { get; }
    public string FileName { get; }
    public string RelativePath { get; }
    public FileIconDefinition FileIcon { get; }

    public bool IsReplaceModeEnabled => Parent.IsReplaceModeEnabled;

    public string ReplaceInFileTooltip { get; }

    [ObservableProperty]
    private int _matchCount;

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<SearchMatchLineViewModel> Matches { get; } = new();

    public SearchFileResultViewModel(SearchFileResult result, SearchPanelViewModel parent, IWorkspaceWrapper workspaceWrapper)
    {
        Parent = parent;
        Resource = result.Resource;
        FileName = result.FileName;
        RelativePath = result.RelativePath;
        MatchCount = result.Matches.Count;

        // Use cached tooltip from parent to avoid ServiceLocator call per item
        ReplaceInFileTooltip = parent.ReplaceInFileTooltip;

        // Get the file icon from the explorer service
        var explorerService = workspaceWrapper.WorkspaceService.ExplorerService;
        FileIcon = explorerService.GetIconForResource(result.Resource);

        // Create all match ViewModels and add to collection
        // Using constructor initialization avoids per-item CollectionChanged events
        foreach (var match in result.Matches)
        {
            Matches.Add(new SearchMatchLineViewModel(match, this));
        }

        // Subscribe to parent's property changes to update IsReplaceModeEnabled binding
        Parent.PropertyChanged += Parent_PropertyChanged;
    }

    private void Parent_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchPanelViewModel.IsReplaceModeEnabled))
        {
            OnPropertyChanged(nameof(IsReplaceModeEnabled));
        }
    }

    /// <summary>
    /// Removes a match from this file's results.
    /// </summary>
    public void RemoveMatch(SearchMatchLineViewModel match)
    {
        if (Matches.Remove(match))
        {
            MatchCount = Matches.Count;
        }
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void NavigateToFile()
    {
        if (Matches.Count > 0)
        {
            var firstMatch = Matches[0];
            var startColumn = firstMatch.OriginalMatchStart + 1;
            var endColumn = startColumn + firstMatch.MatchLength;
            Parent.NavigateToResult(Resource, firstMatch.LineNumber, startColumn, firstMatch.LineNumber, endColumn);
        }
    }

    [RelayCommand]
    private async Task ReplaceInFile()
    {
        await Parent.ReplaceInFileAsync(this);
    }
}
