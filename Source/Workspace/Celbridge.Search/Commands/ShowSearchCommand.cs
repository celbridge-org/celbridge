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
        if (!_layoutService.IsContextPanelVisible)
        {
            _layoutService.SetRegionVisibility(LayoutRegion.Primary, true);
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

    public static void Search(string searchText, bool matchCase = false, bool wholeWord = false)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IShowSearchCommand>(command =>
        {
            command.SearchText = searchText;
            command.MatchCase = matchCase;
            command.WholeWord = wholeWord;
        });
    }

    public static void SearchAndReplace(string searchText, string replaceText, bool matchCase = false, bool wholeWord = false)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IShowSearchCommand>(command =>
        {
            command.SearchText = searchText;
            command.MatchCase = matchCase;
            command.WholeWord = wholeWord;
            command.ReplaceMode = true;
            command.ReplaceText = replaceText;
        });
    }
}
