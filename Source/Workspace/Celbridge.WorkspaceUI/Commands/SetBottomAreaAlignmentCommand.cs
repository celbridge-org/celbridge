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
        _layoutService.SetBottomAreaAlignment(Alignment);

        await Task.CompletedTask;
        return Result.Ok();
    }
}
