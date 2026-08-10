using Celbridge.Commands;

namespace Celbridge.WorkspaceUI.Commands;

public class SetSurfaceVisibilityCommand : CommandBase, ISetSurfaceVisibilityCommand
{
    private readonly ILayoutService _layoutService;

    public WorkspaceSurface Surfaces { get; set; }

    public bool IsVisible { get; set; }

    public SetSurfaceVisibilityCommand(ILayoutService layoutService)
    {
        _layoutService = layoutService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        _layoutService.SetSurfaceVisibility(Surfaces, IsVisible);

        await Task.CompletedTask;
        return Result.Ok();
    }
}
