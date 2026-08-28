using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class GetUtilitiesStateCommand : CommandBase, IGetUtilitiesStateCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public UtilitiesStateSnapshot ResultValue { get; private set; }
        = new UtilitiesStateSnapshot(Array.Empty<UtilityInfo>());

    public GetUtilitiesStateCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
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

        // Which rail button is selected is the panel's own state; where each item lives is the register's.
        var activeUtilityId = workspaceService.UtilityPanel.ActiveUtilityId;
        var activeDocument = documentsService.ActiveDocument;

        var utilities = new List<UtilityInfo>();

        foreach (var railItem in utilityService.GetRailItems())
        {
            var resource = ResourceKey.Empty;
            if (railItem.Resource is not null)
            {
                resource = railItem.Resource.Resource;
            }

            var area = utilityService.GetItemArea(railItem.ItemId);

            // An item in the panel is shown when the rail has selected it; anywhere else it is a document tab,
            // shown when it is the active document.
            bool isShown;
            if (area == WorkspaceArea.Utility)
            {
                isShown = activeUtilityId == railItem.ItemId;
            }
            else
            {
                isShown = !resource.IsEmpty
                    && activeDocument == resource;
            }

            utilities.Add(new UtilityInfo(
                railItem.ItemId,
                railItem.DisplayName,
                area,
                isShown,
                resource));
        }

        ResultValue = new UtilitiesStateSnapshot(utilities);

        return Result.Ok();
    }
}
