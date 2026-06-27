using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.WorkspaceUI.Commands;

public class PerformEditCommand : CommandBase, IPerformEditCommand
{
    private readonly IFocusService _focusService;

    public EditIntent Intent { get; set; }

    public PerformEditCommand(IFocusService focusService)
    {
        _focusService = focusService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        var target = _focusService.EditTarget;
        if (target is not null
            && target.CanPerformEdit(Intent))
        {
            target.PerformEdit(Intent);
        }

        return Result.Ok();
    }
}
