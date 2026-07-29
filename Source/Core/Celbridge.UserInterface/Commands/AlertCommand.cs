using Celbridge.Commands;
using Celbridge.Dialog;

namespace Celbridge.UserInterface.Commands;

public class AlertCommand : CommandBase, IAlertCommand
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public override async Task<Result> ExecuteAsync()
    {
        var dialogService = ServiceLocator.AcquireService<IDialogService>();
        await dialogService.ShowAlertDialogAsync(Title, Message);

        return Result.Ok();
    }
}
