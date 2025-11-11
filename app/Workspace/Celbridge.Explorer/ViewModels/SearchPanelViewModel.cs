using System.Collections.ObjectModel;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Explorer.ViewModels;

public partial class SearchLineResults : ObservableObject
{
    [ObservableProperty]
    public int _lineNumber = 0;

    [ObservableProperty]
    public string _excerpt = string.Empty;
}

public partial class SearchResults : ObservableObject
{
    [ObservableProperty]
    public IconDefinition _icon;

    [ObservableProperty]
    public string _fileName = string.Empty;

    [ObservableProperty]
    public string _filePath = string.Empty;

    [ObservableProperty]
    public int _hitCount = 0;
    /*
        public int _hitCount
        {
            get
            {
                return _lineResults.Count;
            }
        }
    */
    [ObservableProperty]
    public ObservableCollection<SearchLineResults> _lineResults = new ObservableCollection<SearchLineResults>();
}

public partial class SearchPanelViewModel : ObservableObject
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    [ObservableProperty]
    public ObservableCollection<SearchResults> _results = new ObservableCollection<SearchResults>();

    // %%% MODIFY THE CALLING AREA TO USE LISTS INSTEAD OF OBSERVABLE COLLECTIONS.
    public void SetSearchResults(ObservableCollection<SearchResults> results)
    {
        Results.Clear();
        foreach (var result in results)
        {
            var newResult = new SearchResults
            {
                Icon = result.Icon,
                FileName = result.FileName,
                FilePath = result.FilePath,
                HitCount = result.HitCount,
                LineResults = new ObservableCollection<SearchLineResults>(result.LineResults)
            };

            Results.Add(newResult);
        }
    }

    public SearchPanelViewModel(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }
}
