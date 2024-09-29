using Celbridge.Dialog;

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
}

