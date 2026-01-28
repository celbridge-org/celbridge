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

    //
    // Static methods for scripting support.
    //

    public static void Alert(string title, string message)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IAlertCommand>(command =>
        {
            command.Title = title;
            command.Message = message;
        });
    }

    public static void Alert(string message)
    {
        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        var titleString = stringLocalizer.GetString("AlertDialog_TitleDefault");

        Alert(titleString, message);
    }
}
