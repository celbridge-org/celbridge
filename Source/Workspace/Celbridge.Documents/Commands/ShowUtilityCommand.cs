using Celbridge.Commands;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class ShowUtilityCommand : CommandBase, IShowUtilityCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public EditorId UtilityId { get; set; } = EditorId.Empty;

    public WorkspaceArea? TargetArea { get; set; }

    public ShowUtilityCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (UtilityId.IsEmpty)
        {
            return Result.Fail("Cannot show utility: UtilityId is empty");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var utilityPanel = workspaceService.UtilityPanel;

        // Only a workspace-scoped utility can move between areas, so a requested area applies to those alone.
        // A utility that was declared but skipped at load is not live and has no button on the rail.
        var utilityService = workspaceService.UtilityService;
        if (utilityService.HasUtility(UtilityId))
        {
            if (TargetArea is not null)
            {
                var targetArea = TargetArea.Value;

                var dockResult = await utilityService.DockUtilityAsync(UtilityId, targetArea);
                if (dockResult.IsFailure)
                {
                    return Result.Fail($"Failed to dock utility '{UtilityId}' in the '{targetArea.ToToken()}' area")
                        .WithErrors(dockResult);
                }
            }
        }
        else
        {
            if (!utilityPanel.HasRailItem(UtilityId))
            {
                return Result.Fail($"No utility found with id '{UtilityId}'");
            }

            if (TargetArea is not null)
            {
                var targetArea = TargetArea.Value;

                var areaResult = CheckRequestedArea(utilityService, targetArea);
                if (areaResult.IsFailure)
                {
                    return areaResult;
                }
            }
        }

        // Reveal the utility wherever it now lives: ShowUtility shows it in the panel when it is there,
        // activates its document tab when it is docked as a document, and opens a launcher's document.
        utilityPanel.ShowUtility(UtilityId);

        return Result.Ok();
    }

    // A rail item that is not a live utility cannot be moved between areas: it opens in the area it
    // declares, or in the Utility Panel when it declares none. Naming that area is a reveal, and naming any
    // other one fails rather than being quietly dropped.
    private Result CheckRequestedArea(IUtilityService utilityService, WorkspaceArea requestedArea)
    {
        var railItem = utilityService.GetRailItems()
            .FirstOrDefault(item => item.ItemId == UtilityId);
        if (railItem is null)
        {
            return Result.Ok();
        }

        // A launcher declares where its document opens, and a panel utility only ever occupies the panel.
        var openArea = railItem.DockArea ?? WorkspaceArea.Utility;
        if (requestedArea == openArea)
        {
            return Result.Ok();
        }

        // The user can move a launcher's tab after it opens, so where it is now counts too.
        var currentArea = utilityService.GetCurrentArea(UtilityId);
        if (requestedArea == currentArea)
        {
            return Result.Ok();
        }

        return Result.Fail(
            $"Cannot move '{UtilityId}' to the '{requestedArea.ToToken()}' area: " +
            $"it is not a utility that moves between areas, and opens in the '{openArea.ToToken()}' area.");
    }
}
