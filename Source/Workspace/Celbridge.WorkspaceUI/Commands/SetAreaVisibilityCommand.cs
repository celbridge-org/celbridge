using Celbridge.Commands;

namespace Celbridge.WorkspaceUI.Commands;

public class SetAreaVisibilityCommand : CommandBase, ISetAreaVisibilityCommand
{
    private readonly ILayoutService _layoutService;

    public WorkspaceArea Area { get; set; }

    public bool IsVisible { get; set; }

    public SetAreaVisibilityCommand(ILayoutService layoutService)
    {
        _layoutService = layoutService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var visibilityResult = _layoutService.SetAreaVisibility(Area, IsVisible);

        await Task.CompletedTask;
        return visibilityResult;
    }
}
