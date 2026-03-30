using Celbridge.Commands;
using Celbridge.Dialog;

namespace Celbridge.UserInterface.Commands;

public class ConfirmActionCommand : CommandBase, IConfirmActionCommand
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ConfirmActionResult ResultValue { get; private set; } = new ConfirmActionResult(false);

    public override async Task<Result> ExecuteAsync()
    {
        var dialogService = ServiceLocator.AcquireService<IDialogService>();
        var confirmResult = await dialogService.ShowConfirmationDialogAsync(Title, Message);

        if (confirmResult.IsFailure)
        {
            return Result.Fail(confirmResult.Error);
        }

        ResultValue = new ConfirmActionResult(confirmResult.Value);
        return Result.Ok();
    }
}
