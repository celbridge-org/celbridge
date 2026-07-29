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
}
