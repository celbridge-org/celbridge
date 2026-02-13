using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class ResetSectionsCommand : CommandBase, IResetSectionsCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResetSectionsCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var documentsPanel = _workspaceWrapper.WorkspaceService.DocumentsPanel;

        await documentsPanel.ResetSectionRatiosAsync();

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void ResetSections()
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();
        commandService.Execute<IResetSectionsCommand>();
    }
}
