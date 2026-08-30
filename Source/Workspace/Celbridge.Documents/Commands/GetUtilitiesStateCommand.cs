using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class GetUtilitiesStateCommand : CommandBase, IGetUtilitiesStateCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILayoutService _layoutService;

    public UtilitiesStateSnapshot ResultValue { get; private set; }
        = new UtilitiesStateSnapshot(Array.Empty<UtilityInfo>());

    public GetUtilitiesStateCommand(
        IWorkspaceWrapper workspaceWrapper,
        ILayoutService layoutService)
    {
        _workspaceWrapper = workspaceWrapper;
        _layoutService = layoutService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            ResultValue = new UtilitiesStateSnapshot(Array.Empty<UtilityInfo>());
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var utilityService = workspaceService.UtilityService;
        var documentsService = workspaceService.DocumentsService;

        // Which rail button is selected is the panel's own state. Where each item lives is the register's.
        var activeUtilityId = workspaceService.UtilityPanel.ActiveUtilityId;

        var utilities = new List<UtilityInfo>();

        foreach (var railItem in utilityService.GetRailItems())
        {
            var currentArea = utilityService.GetCurrentArea(railItem.ItemId);

            utilities.Add(new UtilityInfo(
                railItem.ItemId,
                railItem.DisplayName,
                currentArea,
                railItem.DockArea,
                IsItemVisible(railItem, currentArea, activeUtilityId, documentsService),
                railItem.FileResource));
        }

        ResultValue = new UtilitiesStateSnapshot(utilities);

        return Result.Ok();
    }

    // Whether the user can see the item: something has to be presenting it, in an area that is not
    // collapsed. The rail keeps its selection through a collapse so a reveal returns to it, which is why
    // being selected is not on its own enough.
    private bool IsItemVisible(
        UtilityRailItem railItem,
        WorkspaceArea? currentArea,
        EditorId activeUtilityId,
        IDocumentsService documentsService)
    {
        if (currentArea is null)
        {
            return false;
        }

        if (!_layoutService.IsAreaVisible(currentArea.Value))
        {
            return false;
        }

        if (currentArea == WorkspaceArea.Utility)
        {
            return activeUtilityId == railItem.ItemId;
        }

        // Each section shows its own selected tab, so a document is on screen whenever it is the one its
        // section is showing. The active document is a single workspace-wide choice and says nothing about
        // what the other areas are drawing.
        var openDocument = documentsService.FindOpenDocument(railItem.FileResource);
        if (openDocument is null)
        {
            return false;
        }

        return documentsService.GetSelectedDocument(openDocument.Address.Section) == railItem.FileResource;
    }
}
