using Celbridge.Commands;
using Celbridge.Dialog;

namespace Celbridge.UserInterface.Commands;

public class ShowAboutCommand : CommandBase, IShowAboutCommand
{
    public override async Task<Result> ExecuteAsync()
    {
        var dialogService = ServiceLocator.AcquireService<IDialogService>();
        await dialogService.ShowAboutDialogAsync();

        return Result.Ok();
    }
}
