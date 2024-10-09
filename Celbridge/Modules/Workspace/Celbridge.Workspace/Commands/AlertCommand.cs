using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Foundation;
using Microsoft.Extensions.Localization;

namespace Celbridge.Workspace.Commands;

public class AlertCommand : CommandBase, IAlertCommand
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public override async Task<Result> ExecuteAsync()
    {
        var dialogService = ServiceLocator.ServiceProvider.GetRequiredService<IDialogService>();
        await dialogService.ShowAlertDialogAsync(Title, Message);

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Alert(string title, string message)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAlertCommand>(command =>
        {
            command.Title = title;
            command.Message = message;
        });
    }

    public static void Alert(string message)
    {
        var stringLocalizer = ServiceLocator.ServiceProvider.GetRequiredService<IStringLocalizer>();
        var titleString = stringLocalizer.GetString("WorkspacePage_AlertTitleDefault");

        Alert(titleString, message);
    }
}
