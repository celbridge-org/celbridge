using System.ComponentModel;

namespace Celbridge.UserInterface.ViewModels;

public partial class IconPickerDialogViewModel : ObservableObject
{
    private readonly IIconService _iconService;

    private List<IconPickerItem> _allItems = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private List<IconPickerItem> _filteredItems = [];

    [ObservableProperty]
    private IconPickerItem? _selectedItem;

    [ObservableProperty]
    private bool _isSubmitEnabled = false;

    [ObservableProperty]
    private bool _isEmptyMessageVisible = false;

    public IconPickerDialogViewModel(IIconService iconService)
    {
        _iconService = iconService;
        PropertyChanged += OnPropertyChanged;
    }

    public void Initialize(string selectedIconName)
    {
        _allItems = _iconService.GetSupportedIcons()
            .Select(catalogEntry => new IconPickerItem(catalogEntry))
            .ToList();

        UpdateFilteredItems();

        // A name the supported set does not carry leaves the list unselected, so the dialog opens on the
        // full list rather than on nothing.
        SelectedItem = _allItems.FirstOrDefault(item => item.IconName == selectedIconName);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchText))
        {
            UpdateFilteredItems();
        }
        else if (e.PropertyName == nameof(SelectedItem))
        {
            IsSubmitEnabled = SelectedItem is not null;
        }
    }

    private void UpdateFilteredItems()
    {
        var search = SearchText.Trim();

        if (string.IsNullOrEmpty(search))
        {
            FilteredItems = [.. _allItems];
            IsEmptyMessageVisible = false;
            return;
        }

        var searchLower = search.ToLowerInvariant();

        // An icon named for the search term is more likely the one the user wants than an icon that
        // merely carries the term as a keyword, so the two groups are listed in that order. The icons
        // arrive sorted by name, so each group keeps that order.
        var nameMatches = new List<IconPickerItem>();
        var keywordMatches = new List<IconPickerItem>();

        foreach (var item in _allItems)
        {
            if (item.IconNameLower.Contains(searchLower))
            {
                nameMatches.Add(item);
            }
            else if (item.KeywordTextLower.Contains(searchLower))
            {
                keywordMatches.Add(item);
            }
        }

        var filteredItems = new List<IconPickerItem>(nameMatches);
        filteredItems.AddRange(keywordMatches);

        FilteredItems = filteredItems;
        IsEmptyMessageVisible = filteredItems.Count == 0;
    }
}
