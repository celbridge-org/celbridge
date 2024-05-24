using Celbridge.BaseLibrary.UserInterface;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.ScriptUtils;

public class CommonUtils
{
    public static void Alert(string title, string message)
    {
        // UI code must be run on the UI thread
        var dispatcher = ServiceLocator.ServiceProvider.GetRequiredService<IDispatcher>();
        dispatcher.ExecuteAsync(() =>
        {
            var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
            var dialogService = userInterfaceService.DialogService;

            dialogService.ShowAlertDialogAsync(title, message, "Ok");
        });
    }

    public static void NewProject()
    {
        // UI code must be run on the UI thread
        var dispatcher = ServiceLocator.ServiceProvider.GetRequiredService<IDispatcher>();
        dispatcher.ExecuteAsync(() =>
        {
            var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
            var dialogService = userInterfaceService.DialogService;

            dialogService.ShowNewProjectDialogAsync();
        });
    }
}

