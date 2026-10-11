using Celbridge.Automation;
using Celbridge.Commands;

namespace Celbridge.WorkspaceUI.Commands;

public class PerformEditCommand : CommandBase, IPerformEditCommand
{
    private readonly IFocusService _focusService;
    private readonly IAutomationService _automationService;

    public EditIntent Intent { get; set; }

    public PerformEditCommand(
        IFocusService focusService,
        IAutomationService automationService)
    {
        _focusService = focusService;
        _automationService = automationService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        var target = _focusService.EditTarget;
        if (target is not null
            && target.CanPerformEdit(Intent))
        {
            target.PerformEdit(Intent);
            _automationService.LogEvent(new EditCommandPerformedEvent(Intent));
        }

        return Result.Ok();
    }
}
