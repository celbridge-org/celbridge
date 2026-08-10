using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Search.Commands;

public class ShowSearchCommand : CommandBase, IShowSearchCommand
{
    private readonly ILayoutService _layoutService;

    public string SearchText { get; set; } = string.Empty;

    public bool MatchCase { get; set; }

    public bool WholeWord { get; set; }

    public bool ReplaceMode { get; set; }

    public string ReplaceText { get; set; } = string.Empty;

    public ShowSearchCommand(
        ILayoutService layoutService)
    {
        _layoutService = layoutService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // Ensure the primary region (which contains search) is visible
        if (!_layoutService.IsUtilityPanelVisible)
        {
            _layoutService.SetRegionVisibility(LayoutRegion.UtilityPanel, true);
        }

        var searchPanel = ServiceLocator.AcquireService<ISearchPanel>();

        // Configure search options
        searchPanel.SetMatchCase(MatchCase);
        searchPanel.SetWholeWord(WholeWord);

        // Configure replace mode if requested
        searchPanel.SetReplaceMode(ReplaceMode);
        if (ReplaceMode)
        {
            searchPanel.SetReplaceText(ReplaceText);
        }

        // Set search text and execute search
        searchPanel.SetSearchText(SearchText);
        searchPanel.ExecuteSearch();

        searchPanel.FocusSearchInput();

        await Task.CompletedTask;
        return Result.Ok();
    }
}
