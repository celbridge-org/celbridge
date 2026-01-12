using Celbridge.Commands;

namespace Celbridge.UserInterface.Commands;

public class ToggleFullScreenCommand : CommandBase, IToggleFullScreenCommand
{
    private readonly ILayoutManager _layoutManager;

    public ToggleFullScreenCommand(ILayoutManager layoutManager)
    {
        _layoutManager = layoutManager;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var result = _layoutManager.RequestTransition(LayoutTransition.ToggleZenMode);
        
        await Task.CompletedTask;

        return result;
    }
}
