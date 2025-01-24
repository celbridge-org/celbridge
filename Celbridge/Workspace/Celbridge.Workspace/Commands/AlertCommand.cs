using Celbridge.Commands;
using Celbridge.Dialog;
using Microsoft.Extensions.Localization;

namespace Celbridge.Workspace.Commands;

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
        var titleString = stringLocalizer.GetString("WorkspacePage_AlertTitleDefault");

        Alert(titleString, message);
    }
}
