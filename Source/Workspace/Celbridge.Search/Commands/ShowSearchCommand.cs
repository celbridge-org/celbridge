using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Search.Commands;

public class ShowSearchCommand : CommandBase, IShowSearchCommand
{
    private readonly ILayoutService _layoutService;
    private readonly ICommandService _commandService;

    public string SearchText { get; set; } = string.Empty;

    public bool MatchCase { get; set; }

    public bool WholeWord { get; set; }

    public bool ReplaceMode { get; set; }

    public string ReplaceText { get; set; } = string.Empty;

    public ShowSearchCommand(
        ILayoutService layoutService,
        ICommandService commandService)
    {
        _layoutService = layoutService;
        _commandService = commandService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // Restore console if maximized so user can see the search panel
        if (_layoutService.IsConsoleMaximized)
        {
            _commandService.Execute<ISetConsoleMaximizedCommand>(command =>
            {
                command.IsMaximized = false;
            });
        }

        // Ensure the primary region (which contains search) is visible
        if (!_layoutService.IsContextPanelVisible)
        {
            _layoutService.SetRegionVisibility(LayoutRegion.Primary, true);
        }

        // Get the search panel
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

        // Focus the search input
        searchPanel.FocusSearchInput();

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

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
