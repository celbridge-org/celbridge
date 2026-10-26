using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Search.Commands;

public class SearchFolderCommand : CommandBase, ISearchFolderCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Folder { get; set; } = ResourceKey.Empty;

    public SearchFolderCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        // Present the Search surface, which brings the Utility Panel back when it is collapsed.
        var utilityPanel = _workspaceWrapper.WorkspaceService.UtilityPanel;
        utilityPanel.ShowUtility(BuiltInUtilityIds.Search);

        var searchPanel = utilityPanel.SearchPanel;
        searchPanel.SetFolder(Folder);

        // The search runs only when the search field has text.
        searchPanel.ExecuteSearch();

        searchPanel.FocusSearchInput();

        return Result.Ok();
    }
}
