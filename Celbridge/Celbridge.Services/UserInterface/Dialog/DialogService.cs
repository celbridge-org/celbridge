using Celbridge.BaseLibrary.UserInterface.Dialog;

namespace Celbridge.Services.UserInterface.Dialog;

public class DialogService : IDialogService
{
    ILoggingService _loggingService;

    public DialogService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public async Task ShowAlertAsync(string message)
    {
        await Task.Delay(500);

        _loggingService.Info(message);
    }
}
