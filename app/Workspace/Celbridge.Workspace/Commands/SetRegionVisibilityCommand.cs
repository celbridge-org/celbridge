using Celbridge.Commands;
using Celbridge.UserInterface;

namespace Celbridge.Workspace.Commands;

public class SetRegionVisibilityCommand : CommandBase, ISetRegionVisibilityCommand
{
    private readonly ILayoutManager _layoutManager;

    public LayoutRegion Regions { get; set; }

    public bool IsVisible { get; set; }

    public SetRegionVisibilityCommand(ILayoutManager layoutManager)
    {
        _layoutManager = layoutManager;
    }

    public override async Task<Result> ExecuteAsync()
    {
        _layoutManager.SetRegionVisibility(Regions, IsVisible);

        await Task.CompletedTask;
        return Result.Ok();
    }
}
