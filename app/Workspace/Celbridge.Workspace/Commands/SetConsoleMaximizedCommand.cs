using Celbridge.Commands;
using Celbridge.UserInterface;

namespace Celbridge.Workspace.Commands;

public class SetConsoleMaximizedCommand : CommandBase, ISetConsoleMaximizedCommand
{
    private readonly ILayoutManager _layoutManager;

    public bool IsMaximized { get; set; }

    public SetConsoleMaximizedCommand(ILayoutManager layoutManager)
    {
        _layoutManager = layoutManager;
    }

    public override async Task<Result> ExecuteAsync()
    {
        _layoutManager.SetConsoleMaximized(IsMaximized);

        await Task.CompletedTask;
        return Result.Ok();
    }
}
