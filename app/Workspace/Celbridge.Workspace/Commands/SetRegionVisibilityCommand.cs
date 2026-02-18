using Celbridge.Commands;

namespace Celbridge.Workspace.Commands;

public class SetRegionVisibilityCommand : CommandBase, ISetRegionVisibilityCommand
{
    private readonly ILayoutService _layoutService;

    public LayoutRegion Regions { get; set; }

    public bool IsVisible { get; set; }

    public SetRegionVisibilityCommand(ILayoutService layoutService)
    {
        _layoutService = layoutService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        _layoutService.SetRegionVisibility(Regions, IsVisible);

        await Task.CompletedTask;
        return Result.Ok();
    }
}
