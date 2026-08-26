using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Search.Commands;

public class ShowSearchCommand : CommandBase, IShowSearchCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public string SearchText { get; set; } = string.Empty;

    public bool MatchCase { get; set; }

    public bool WholeWord { get; set; }

    public bool ReplaceMode { get; set; }

    public string ReplaceText { get; set; } = string.Empty;

    public ShowSearchCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // Present the Search surface, which brings the Utility Panel back when it is collapsed.
        var utilityPanel = _workspaceWrapper.WorkspaceService.UtilityPanel;
        utilityPanel.ShowUtility(BuiltInUtilityIds.Search);

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
