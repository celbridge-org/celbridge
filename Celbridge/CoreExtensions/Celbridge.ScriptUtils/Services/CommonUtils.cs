using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.ScriptUtils.Services;

public class CommonUtils
{
    public static void Alert(string title, string message)
    {
        // UI code must be run on the UI thread
        var dispatcher = ServiceLocator.ServiceProvider.GetRequiredService<IDispatcher>();
        dispatcher.ExecuteAsync(() =>
        {
            var dialogService = ServiceLocator.ServiceProvider.GetRequiredService<IDialogService>();
            dialogService.ShowAlertDialogAsync(title, message);
        });
    }

    public static void Undo(string stackName)
    {
        var dispatcher = ServiceLocator.ServiceProvider.GetRequiredService<IDispatcher>();
        dispatcher.ExecuteAsync(() =>
        {
            var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
            commandService.Undo(stackName);
        });
    }

    public static void Redo(string stackName)
    {
        var dispatcher = ServiceLocator.ServiceProvider.GetRequiredService<IDispatcher>();
        dispatcher.ExecuteAsync(() =>
        {
            var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
            commandService.Redo(stackName);
        });
    }
}

