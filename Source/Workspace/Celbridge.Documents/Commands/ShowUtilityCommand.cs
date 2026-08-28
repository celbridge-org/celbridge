using Celbridge.Commands;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class ShowUtilityCommand : CommandBase, IShowUtilityCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public EditorId UtilityId { get; set; } = EditorId.Empty;

    public ShowUtilityArea? Area { get; set; }

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
        // Guard against the live utilities rather than the declared contributions: a utility that was declared
        // but skipped at load has no button on the rail and cannot be shown.
        var utilityService = workspaceService.UtilityService;
        if (utilityService.HasUtility(UtilityId))
        {
            if (Area is not null)
            {
                var resolveResult = ResolveArea(utilityService, Area);
                if (resolveResult.IsFailure)
                {
                    return Result.Fail($"Cannot show utility '{UtilityId}'")
                        .WithErrors(resolveResult);
                }
                var targetArea = resolveResult.Value;

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

    // A named area is taken as it stands, and DockUtilityAsync rejects one the utility does not allow. A
    // request for the utility's own document area is answered from its declaration, which is why the caller
    // does not have to know which document areas it offers.
    private Result<WorkspaceArea> ResolveArea(IUtilityService utilityService, ShowUtilityArea area)
    {
        var namedArea = area.NamedArea;
        if (namedArea is not null)
        {
            return namedArea.Value;
        }

        var railItem = utilityService.GetRailItems().FirstOrDefault(item => item.ItemId == UtilityId);
        if (railItem is null)
        {
            return Result.Fail($"No rail item found with id '{UtilityId}'");
        }

        if (!WorkspaceAreaHelper.TryGetDocumentArea(railItem.AllowedAreas, railItem.DefaultArea, out var documentArea))
        {
            return Result.Fail(
                $"Utility '{UtilityId}' does not name one document area to open in. " +
                $"Ask for one of its areas by name instead.");
        }

        return documentArea;
    }
}
