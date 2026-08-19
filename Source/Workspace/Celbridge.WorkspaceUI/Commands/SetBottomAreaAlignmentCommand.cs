using Celbridge.Commands;

namespace Celbridge.WorkspaceUI.Commands;

public class SetBottomAreaAlignmentCommand : CommandBase, ISetBottomAreaAlignmentCommand
{
    private readonly ILayoutService _layoutService;

    public BottomAreaAlignment Alignment { get; set; }

    public SetBottomAreaAlignmentCommand(ILayoutService layoutService)
    {
        _layoutService = layoutService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // The alignment is applied before the reveal so a hidden area appears at its new span rather than
        // laying out twice.
        _layoutService.SetBottomAreaAlignment(Alignment);
        _layoutService.SetSurfaceVisibility(WorkspaceSurface.BottomArea, true);

        await Task.CompletedTask;
        return Result.Ok();
    }
}
