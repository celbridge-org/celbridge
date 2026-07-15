using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class DockUtilityCommand : CommandBase, IDockUtilityCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public UtilityId UtilityId { get; set; } = UtilityId.Empty;

    public DockLocation Location { get; set; } = DockLocation.Document;

    public DockUtilityCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (UtilityId.IsEmpty)
        {
            return Result.Fail("Cannot dock utility: UtilityId is empty");
        }

        var utilityService = _workspaceWrapper.WorkspaceService.UtilityService;
        return await utilityService.DockUtilityAsync(UtilityId, Location);
    }
}
