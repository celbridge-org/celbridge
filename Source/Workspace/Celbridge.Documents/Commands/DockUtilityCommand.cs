using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class DockUtilityCommand : CommandBase, IDockUtilityCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public EditorId UtilityId { get; set; } = EditorId.Empty;

    public WorkspaceArea Area { get; set; } = WorkspaceArea.Main;

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
        return await utilityService.DockUtilityAsync(UtilityId, Area);
    }
}
