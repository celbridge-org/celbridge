using Celbridge.Commands;
using Celbridge.UserInterface;

namespace Celbridge.Workspace.Commands;

public class SetPanelVisibilityCommand : CommandBase, ISetPanelVisibilityCommand
{
    private readonly ILayoutManager _layoutManager;

    public PanelVisibilityFlags Panels { get; set; }

    public bool IsVisible { get; set; }

    public SetPanelVisibilityCommand(ILayoutManager layoutManager)
    {
        _layoutManager = layoutManager;
    }

    public override async Task<Result> ExecuteAsync()
    {
        _layoutManager.SetPanelVisibility(Panels, IsVisible);

        await Task.CompletedTask;
        return Result.Ok();
    }
}
