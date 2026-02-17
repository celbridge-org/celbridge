using Celbridge.Commands;

namespace Celbridge.UserInterface.Commands;

public class SetLayoutCommand : CommandBase, ISetLayoutCommand
{
    private readonly ILayoutManager _layoutManager;

    public WindowModeTransition Transition { get; set; }

    public SetLayoutCommand(ILayoutManager layoutManager)
    {
        _layoutManager = layoutManager;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var result = _layoutManager.RequestWindowModeTransition(Transition);
        
        await Task.CompletedTask;

        return result;
    }
}
