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
        var activeDocument = documentsService.ActiveDocument;

        var utilities = new List<UtilityInfo>();

        foreach (var railItem in utilityService.GetRailItems())
        {
            var resource = railItem.FileResource;

            var currentArea = utilityService.GetCurrentArea(railItem.ItemId);

            // An item in the panel is selected when the rail has selected it. In a document area it is a
            // tab, selected when it is the active document. Occupying no area at all, it is neither.
            bool isSelected;
            if (currentArea is null)
            {
                isSelected = false;
            }
            else if (currentArea == WorkspaceArea.Utility)
            {
                isSelected = activeUtilityId == railItem.ItemId;
            }
            else
            {
                isSelected = !resource.IsEmpty
                    && activeDocument == resource;
            }

            // Being selected is not the same as being on screen: a collapsed area shows nothing, and the
            // rail keeps its selection through a collapse so a reveal returns to it.
            bool isVisible = isSelected
                && currentArea is not null
                && _layoutService.IsAreaVisible(currentArea.Value);

            utilities.Add(new UtilityInfo(
                railItem.ItemId,
                railItem.DisplayName,
                currentArea,
                railItem.DockArea,
                isVisible,
                resource));
        }

        ResultValue = new UtilitiesStateSnapshot(utilities);

        return Result.Ok();
    }
}
