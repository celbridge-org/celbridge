using Celbridge.Commands;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class ShowUtilityCommand : CommandBase, IShowUtilityCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public EditorId UtilityId { get; set; } = EditorId.Empty;

    public WorkspaceArea? Area { get; set; }

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
            if (Area is not null)
            {
                var targetArea = Area.Value;

                var dockResult = await utilityService.DockUtilityAsync(UtilityId, targetArea);
                if (dockResult.IsFailure)
                {
                    return Result.Fail($"Failed to dock utility '{UtilityId}' in the '{targetArea.ToToken()}' area")
                        .WithErrors(dockResult);
                }
            }
        }
        else if (!utilityPanel.HasRailItem(UtilityId))
        {
            return Result.Fail($"No utility found with id '{UtilityId}'");
        }

        // Reveal the utility wherever it now lives: ShowUtility shows its panel surface when it is in the panel,
        // activates its document tab when it is docked as a document, and opens a launcher's document.
        utilityPanel.ShowUtility(UtilityId);

        return Result.Ok();
    }
}
