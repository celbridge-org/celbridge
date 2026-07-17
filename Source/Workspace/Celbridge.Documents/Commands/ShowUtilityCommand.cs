using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class ShowUtilityCommand : CommandBase, IShowUtilityCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public EditorInstanceId UtilityId { get; set; } = EditorInstanceId.Empty;

    public DockLocation? Location { get; set; }

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

        // Built-in utilities (Explorer, Search) are not contributions and are never docked, so a requested
        // Location is ignored for them; reveal them directly.
        if (UtilityId == BuiltInUtilityIds.Explorer
            || UtilityId == BuiltInUtilityIds.Search)
        {
            utilityPanel.ShowUtility(UtilityId);
            return Result.Ok();
        }

        // Guard against the live utilities rather than the declared contributions: a utility that was declared
        // but skipped at load cannot be shown, and ShowUtility below reveals nothing for an id it does not hold.
        var utilityService = workspaceService.UtilityService;
        if (!utilityService.HasUtility(UtilityId))
        {
            return Result.Fail($"No utility found with id '{UtilityId}'");
        }

        // When a target location is given, move the utility there first; otherwise leave it where it is.
        if (Location is not null)
        {
            var dockResult = await utilityService.DockUtilityAsync(UtilityId, Location.Value);
            if (dockResult.IsFailure)
            {
                return Result.Fail($"Failed to dock utility '{UtilityId}' at location '{Location.Value}'")
                    .WithErrors(dockResult);
            }
        }

        // Reveal the utility wherever it now lives: ShowUtility shows its panel surface when it is in the panel,
        // or activates its document tab when it is docked as a document.
        utilityPanel.ShowUtility(UtilityId);
        return Result.Ok();
    }
}
