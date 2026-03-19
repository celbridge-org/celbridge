using Celbridge.Commands;

namespace Celbridge.UserInterface.Commands;

public class SetLayoutCommand : CommandBase, ISetLayoutCommand
{
    private readonly IWindowModeService _windowModeService;

    public WindowModeTransition Transition { get; set; }

    public SetLayoutCommand(IWindowModeService windowModeService)
    {
        _windowModeService = windowModeService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var result = _windowModeService.RequestWindowModeTransition(Transition);

        await Task.CompletedTask;

        return result;
    }
}
