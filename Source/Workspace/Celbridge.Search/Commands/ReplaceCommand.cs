using Celbridge.Commands;

namespace Celbridge.Search.Commands;

public class ReplaceCommand : CommandBase, IReplaceCommand
{
    public ReplaceScope Scope { get; set; } = ReplaceScope.All;

    public bool? ShowConfirmation { get; set; }

    public override async Task<Result> ExecuteAsync()
    {
        var searchPanel = ServiceLocator.AcquireService<ISearchPanel>();

        if (Scope == ReplaceScope.All)
        {
            await searchPanel.ExecuteReplaceAllAsync();
        }
        else
        {
            await searchPanel.ExecuteReplaceSelectedAsync();
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void All(bool showConfirmation = true)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IReplaceCommand>(command =>
        {
            command.Scope = ReplaceScope.All;
            command.ShowConfirmation = showConfirmation;
        });
    }

    public static void Selected()
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IReplaceCommand>(command =>
        {
            command.Scope = ReplaceScope.Selected;
        });
    }
}
