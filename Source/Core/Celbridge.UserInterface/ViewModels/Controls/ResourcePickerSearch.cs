using Celbridge.Logging;
using Celbridge.Patterns;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// Finds resources across the whole project for the resource picker's search box. The search walks the project once,
/// and keeps the items the picker lists. Each search then matches the typed text against those items' paths.
/// </summary>
internal sealed class ResourcePickerSearch
{
    private readonly ILogger _logger;
    private List<ResourcePickerItem> _items = [];

    public ResourcePickerSearch(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns true once the walk has finished. Until then, a search has nothing to match.
    /// </summary>
    public bool IsWalkFinished { get; private set; }

    /// <summary>
    /// Walks the project on a worker thread, and keeps an item for each resource the picker lists. A walk that fails
    /// leaves the search with nothing to find, and the picker stays usable.
    /// </summary>
    public async Task WalkAsync(IResourceWalker walker, ResourcePickerItemFactory itemFactory)
    {
        try
        {
            var filter = itemFactory.GetWalkFilter();

            _items = await Task.Run(async () =>
            {
                var walkResult = await walker.WalkAsync(ResourceKey.Empty, applySearchExclude: false, filter);
                if (walkResult.IsFailure)
                {
                    _logger.LogWarning(walkResult, "Failed to walk the project for the resource picker");
                    return [];
                }
                var walkedItems = walkResult.Value;

                var items = new List<ResourcePickerItem>();
                foreach (var walkedItem in walkedItems)
                {
                    var item = itemFactory.CreateItem(
                        walkedItem.Resource,
                        walkedItem.IsFolder,
                        walkedItem.Attributes,
                        showsPath: true);
                    if (item is not null)
                    {
                        items.Add(item);
                    }
                }

                return items;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to walk the project for the resource picker");
            _items = [];
        }

        IsWalkFinished = true;
    }

    /// <summary>
    /// Returns the items with a path that matches the search, in the walk's order. A search that is a pattern matches
    /// as a file filter in SearchFilter's dialect. Other text matches any path that contains it, ignoring case. Only
    /// the first matches, up to the count given, get a mark on the part of the path that matched.
    /// </summary>
    public List<ResourcePickerItem> FindMatches(string search, int markedMatchCount)
    {
        var matches = new List<ResourcePickerItem>();
        if (IsWildcardPattern(search))
        {
            // A pattern that is still being typed, like one with an open brace, fails to compile. Such a pattern
            // matches nothing until the user completes it.
            var compileResult = SearchFilter.Compile(search, string.Empty);
            if (compileResult.IsFailure)
            {
                return matches;
            }
            var filter = compileResult.Value;

            foreach (var item in _items)
            {
                if (filter.IsMatch(item.Resource.Path, item.IsFolder))
                {
                    var match = matches.Count < markedMatchCount
                        ? MarkPatternMatch(filter, item)
                        : item;
                    matches.Add(match);
                }
            }

            return matches;
        }

        var searchLower = search.ToLowerInvariant();
        foreach (var item in _items)
        {
            var matchStart = item.PathLower.IndexOf(searchLower, StringComparison.Ordinal);
            if (matchStart >= 0)
            {
                var match = matches.Count < markedMatchCount
                    ? item.WithPathMark(matchStart, searchLower.Length)
                    : item;
                matches.Add(match);
            }
        }

        return matches;
    }

    // A pattern says whether a path matches, but not which characters matched. A matched folder brings everything
    // inside it. So this method tests each folder above the item, starting at the top, and marks the first folder to
    // match the pattern. When no folder above the item matches, the item matched by itself, and the mark goes on the
    // item's name.
    private static ResourcePickerItem MarkPatternMatch(SearchFilter filter, ResourcePickerItem item)
    {
        var path = item.Resource.Path;
        var segmentStart = 0;
        while (true)
        {
            var segmentEnd = path.IndexOf('/', segmentStart);
            if (segmentEnd < 0)
            {
                break;
            }

            if (filter.IsMatch(path[..segmentEnd], isFolder: true))
            {
                return item.WithPathMark(segmentStart, segmentEnd - segmentStart);
            }

            segmentStart = segmentEnd + 1;
        }

        return item.WithPathMark(segmentStart, path.Length - segmentStart);
    }

    // Text with a star or a question mark is a pattern. Other text is plain, so a name with brackets or braces is found
    // as typed.
    private static bool IsWildcardPattern(string search)
    {
        return search.IndexOfAny(['*', '?']) >= 0;
    }
}
