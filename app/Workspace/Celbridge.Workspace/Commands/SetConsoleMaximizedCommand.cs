using Celbridge.Commands;

namespace Celbridge.Workspace.Commands;

public class SetConsoleMaximizedCommand : CommandBase, ISetConsoleMaximizedCommand
{
    private readonly ILayoutService _layoutService;

    public bool IsMaximized { get; set; }

    public SetConsoleMaximizedCommand(ILayoutService layoutService)
    {
        _layoutService = layoutService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        _layoutService.SetConsoleMaximized(IsMaximized);

        await Task.CompletedTask;
        return Result.Ok();
    }
}
